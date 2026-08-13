using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using SummonersVault.Application.Abstractions;
using SummonersVault.Application.Accounts;
using SummonersVault.Core.Models;
using SummonersVault.Infrastructure.Security;
using SummonersVault.Infrastructure.Persistence;
using SummonersVault.Infrastructure.Storage;
using SummonersVault.Application.Security;

namespace SummonersVault.Infrastructure.Backup;

public sealed class VaultBackupService(
    VaultPaths paths,
    IVaultSession session,
    IVaultFileAccess vaultFileAccess,
    IAccountRepository accountRepository) : IBackupService
{
    private const int ArchiveFormatVersion = 1;
    private const long MaximumDatabaseBytes = 1024L * 1024 * 1024;
    private const long MaximumMetadataBytes = 1024 * 1024;
    private const long MaximumManifestBytes = 64 * 1024;
    private const string DatabaseEntryName = "vault.db";
    private const string MetadataEntryName = "vault.meta.json";
    private const string ManifestEntryName = "manifest.json";

    public async Task ExportAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        if (!session.IsUnlocked)
        {
            throw new InvalidOperationException("Unlock the vault before exporting a backup.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        string fullDestinationPath = Path.GetFullPath(destinationPath);
        string destinationDirectory = Path.GetDirectoryName(fullDestinationPath)
            ?? throw new ArgumentException("The backup destination is invalid.", nameof(destinationPath));
        Directory.CreateDirectory(destinationDirectory);

        string temporaryDirectory = CreateTemporaryDirectory();
        string temporaryArchive = Path.Combine(destinationDirectory, $".{Path.GetFileName(fullDestinationPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            string databaseCopy = Path.Combine(temporaryDirectory, DatabaseEntryName);
            string metadataCopy = Path.Combine(temporaryDirectory, MetadataEntryName);
            await vaultFileAccess.WithVaultClosedAsync(async token =>
            {
                File.Copy(paths.DatabasePath, databaseCopy, overwrite: false);
                File.Copy(paths.MetadataPath, metadataCopy, overwrite: false);
                await Task.CompletedTask.ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);

            var manifest = new BackupManifest(
                ArchiveFormatVersion,
                DateTimeOffset.UtcNow,
                DatabaseEntryName,
                await HashFileAsync(databaseCopy, cancellationToken).ConfigureAwait(false),
                MetadataEntryName,
                await HashFileAsync(metadataCopy, cancellationToken).ConfigureAwait(false));

            await using (var stream = new FileStream(temporaryArchive, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.WriteThrough))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
            {
                await AddFileAsync(archive, databaseCopy, DatabaseEntryName, cancellationToken).ConfigureAwait(false);
                await AddFileAsync(archive, metadataCopy, MetadataEntryName, cancellationToken).ConfigureAwait(false);
                ZipArchiveEntry manifestEntry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
                await using Stream manifestStream = manifestEntry.Open();
                await JsonSerializer.SerializeAsync(manifestStream, manifest, cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryArchive, fullDestinationPath, overwrite: true);
        }
        finally
        {
            TryDeleteFile(temporaryArchive);
            TryDeleteDirectory(temporaryDirectory);
        }
    }

    public async Task<BackupImportPreview> PreviewImportAsync(
        string backupPath,
        ReadOnlyMemory<byte> sourceMasterPasswordUtf8,
        CancellationToken cancellationToken = default)
    {
        if (!session.IsUnlocked)
        {
            throw new InvalidOperationException("Unlock the destination vault before importing a backup.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);
        string fullBackupPath = Path.GetFullPath(backupPath);
        if (!File.Exists(fullBackupPath))
        {
            throw new FileNotFoundException("The selected backup was not found.", fullBackupPath);
        }

        string temporaryDirectory = CreateTemporaryDirectory();
        var importedAccounts = new List<AccountImportItem>();
        try
        {
            var sourcePaths = new VaultPaths(temporaryDirectory);
            await ExtractAndValidateArchiveAsync(fullBackupPath, sourcePaths, cancellationToken).ConfigureAwait(false);

            await using var sourceRepository = new EncryptedVaultStore(sourcePaths);
            await using var sourceSession = new VaultSession(sourcePaths, sourceRepository);
            if (!await sourceSession.UnlockAsync(sourceMasterPasswordUtf8, cancellationToken).ConfigureAwait(false))
            {
                throw new UnauthorizedAccessException("The backup master password is incorrect or the backup is damaged.");
            }

            foreach (VaultAccount account in await sourceRepository.GetAccountsAsync(cancellationToken).ConfigureAwait(false))
            {
                SensitiveBuffer password = await sourceRepository.GetPasswordAsync(account.Id, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidDataException("The backup contains an unreadable account.");
                importedAccounts.Add(new(account, password));
            }

            IReadOnlyList<VaultAccount> currentAccounts = await accountRepository.GetAccountsAsync(cancellationToken).ConfigureAwait(false);
            IReadOnlyList<BackupConflict> conflicts = BackupConflictResolver.FindConflicts(importedAccounts, currentAccounts);

            return new BackupImportPreview(temporaryDirectory, importedAccounts, conflicts);
        }
        catch
        {
            foreach (AccountImportItem importedAccount in importedAccounts)
            {
                importedAccount.Password.Dispose();
            }

            TryDeleteDirectory(temporaryDirectory);
            throw;
        }
    }

    public async Task ImportAsync(
        BackupImportPreview preview,
        IReadOnlyDictionary<Guid, BackupConflictChoice> choices,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(choices);
        if (!session.IsUnlocked)
        {
            throw new InvalidOperationException("Unlock the destination vault before importing a backup.");
        }

        if (!Directory.Exists(preview.TemporaryDirectory))
        {
            throw new InvalidDataException("The import preview has expired.");
        }

        IReadOnlyList<VaultAccount> currentAccounts = await accountRepository.GetAccountsAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<AccountImportItem> accountsToMerge = BackupConflictResolver.SelectAccountsToMerge(
            preview,
            choices,
            currentAccounts.Select(account => account.Id));

        await accountRepository.MergeAccountsAsync(accountsToMerge, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExtractAndValidateArchiveAsync(string backupPath, VaultPaths destination, CancellationToken cancellationToken)
    {
        using ZipArchive archive = ZipFile.OpenRead(backupPath);
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (entry.FullName != entry.Name || entry.Name is not (DatabaseEntryName or MetadataEntryName or ManifestEntryName))
            {
                throw new InvalidDataException("The backup contains an invalid archive path.");
            }

            if (!entries.TryAdd(entry.Name, entry))
            {
                throw new InvalidDataException("The backup contains duplicate files.");
            }
        }

        if (!entries.TryGetValue(DatabaseEntryName, out ZipArchiveEntry? databaseEntry)
            || !entries.TryGetValue(MetadataEntryName, out ZipArchiveEntry? metadataEntry)
            || !entries.TryGetValue(ManifestEntryName, out ZipArchiveEntry? manifestEntry))
        {
            throw new InvalidDataException("The backup is missing required files.");
        }

        ValidateSize(databaseEntry, MaximumDatabaseBytes);
        ValidateSize(metadataEntry, MaximumMetadataBytes);
        ValidateSize(manifestEntry, MaximumManifestBytes);
        destination.EnsureCreated();

        BackupManifest? manifest;
        await using (Stream stream = manifestEntry.Open())
        {
            manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        if (manifest is null
            || manifest.FormatVersion != ArchiveFormatVersion
            || manifest.DatabaseFile != DatabaseEntryName
            || manifest.MetadataFile != MetadataEntryName
            || !IsSha256(manifest.DatabaseSha256)
            || !IsSha256(manifest.MetadataSha256))
        {
            throw new InvalidDataException("The backup manifest is invalid or unsupported.");
        }

        await ExtractEntryAsync(databaseEntry, destination.DatabasePath, cancellationToken).ConfigureAwait(false);
        await ExtractEntryAsync(metadataEntry, destination.MetadataPath, cancellationToken).ConfigureAwait(false);

        string databaseHash = await HashFileAsync(destination.DatabasePath, cancellationToken).ConfigureAwait(false);
        string metadataHash = await HashFileAsync(destination.MetadataPath, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(databaseHash, manifest.DatabaseSha256, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(metadataHash, manifest.MetadataSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The backup failed its integrity check.");
        }
    }

    private static void ValidateSize(ZipArchiveEntry entry, long maximumBytes)
    {
        if (entry.Length <= 0 || entry.Length > maximumBytes)
        {
            throw new InvalidDataException("The backup contains a file with an invalid size.");
        }
    }

    private static async Task ExtractEntryAsync(ZipArchiveEntry entry, string destinationPath, CancellationToken cancellationToken)
    {
        await using Stream source = entry.Open();
        await using var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough);
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    private static async Task AddFileAsync(ZipArchive archive, string sourcePath, string entryName, CancellationToken cancellationToken)
    {
        ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using FileStream source = File.OpenRead(sourcePath);
        await using Stream destination = entry.Open();
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(character => Uri.IsHexDigit(character));

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "SummonersVault", "Backup", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record BackupManifest(
        int FormatVersion,
        DateTimeOffset CreatedAtUtc,
        string DatabaseFile,
        string DatabaseSha256,
        string MetadataFile,
        string MetadataSha256);
}
