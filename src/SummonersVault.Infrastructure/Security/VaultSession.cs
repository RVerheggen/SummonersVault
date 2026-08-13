using System.Security.Cryptography;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SummonersVault.Application.Abstractions;
using SummonersVault.Infrastructure.Storage;

namespace SummonersVault.Infrastructure.Security;

public sealed class VaultSession(VaultPaths paths, IVaultStore store) : IVaultSession, IVaultFileAccess
{
    private byte[]? _databaseKey;
    public bool Exists => File.Exists(paths.DatabasePath) && File.Exists(paths.MetadataPath);
    public bool IsUnlocked => _databaseKey is not null && store.IsOpen;

    public async Task CreateAsync(ReadOnlyMemory<byte> masterPasswordUtf8, CancellationToken cancellationToken = default)
    {
        if (Exists)
        {
            throw new InvalidOperationException("A vault already exists.");
        }

        VaultKeyEnvelope.ValidateMasterPassword(masterPasswordUtf8.Span);
        paths.EnsureCreated();
        byte[] databaseKey = RandomNumberGenerator.GetBytes(32);
        try
        {
            await store.OpenAsync(databaseKey, create: true, cancellationToken).ConfigureAwait(false);
            VaultMetadata metadata = VaultKeyEnvelope.Create(Guid.NewGuid(), masterPasswordUtf8.Span, databaseKey);
            await WriteMetadataAtomicAsync(metadata, cancellationToken).ConfigureAwait(false);
            _databaseKey = databaseKey;
            databaseKey = [];
        }
        finally { CryptographicOperations.ZeroMemory(databaseKey); }
    }

    public async Task<bool> UnlockAsync(ReadOnlyMemory<byte> masterPasswordUtf8, CancellationToken cancellationToken = default)
    {
        long unlockStartedAt = Stopwatch.GetTimestamp();
        if (!Exists)
        {
            return false;
        }

        VaultMetadata? metadata = await ReadMetadataAsync(cancellationToken).ConfigureAwait(false);
        long metadataReadAt = Stopwatch.GetTimestamp();
        if (metadata is null || !VaultKeyEnvelope.TryUnwrap(metadata, masterPasswordUtf8.Span, out byte[]? databaseKey))
        {
            WriteUnlockTiming(unlockStartedAt, metadataReadAt, Stopwatch.GetTimestamp(), null);
            return false;
        }

        long keyUnwrappedAt = Stopwatch.GetTimestamp();
        try
        {
            await store.OpenAsync(databaseKey, create: false, cancellationToken).ConfigureAwait(false);
            WriteUnlockTiming(unlockStartedAt, metadataReadAt, keyUnwrappedAt, Stopwatch.GetTimestamp());
            _databaseKey = databaseKey;
            databaseKey = [];
            return true;
        }
        catch (SqliteException)
        {
            await store.CloseAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }
        finally { CryptographicOperations.ZeroMemory(databaseKey); }
    }

    public async Task ChangeMasterPasswordAsync(ReadOnlyMemory<byte> currentPasswordUtf8, ReadOnlyMemory<byte> newPasswordUtf8, CancellationToken cancellationToken = default)
    {
        if (_databaseKey is null)
        {
            throw new InvalidOperationException("Unlock the vault first.");
        }

        VaultKeyEnvelope.ValidateMasterPassword(newPasswordUtf8.Span);
        VaultMetadata metadata = await ReadMetadataAsync(cancellationToken).ConfigureAwait(false) ?? throw new InvalidDataException("Vault metadata is invalid.");
        if (!VaultKeyEnvelope.TryUnwrap(metadata, currentPasswordUtf8.Span, out byte[]? verifiedKey))
        {
            throw new UnauthorizedAccessException("The current master password is incorrect.");
        }

        try
        {
            if (!CryptographicOperations.FixedTimeEquals(verifiedKey, _databaseKey))
            {
                throw new CryptographicException("Vault key verification failed.");
            }

            VaultMetadata replacement = VaultKeyEnvelope.Create(metadata.VaultId, newPasswordUtf8.Span, _databaseKey);
            await WriteMetadataAtomicAsync(replacement, cancellationToken).ConfigureAwait(false);
        }
        finally { CryptographicOperations.ZeroMemory(verifiedKey); }
    }

    public async Task LockAsync(CancellationToken cancellationToken = default)
    {
        await store.CloseAsync(cancellationToken).ConfigureAwait(false);
        if (_databaseKey is not null)
        {
            CryptographicOperations.ZeroMemory(_databaseKey);
            _databaseKey = null;
        }
    }

    internal ReadOnlyMemory<byte> RequireDatabaseKey() => _databaseKey ?? throw new InvalidOperationException("The vault is locked.");

    public async Task WithVaultClosedAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
    {
        byte[] key = _databaseKey ?? throw new InvalidOperationException("The vault is locked.");
        await store.CloseAsync(cancellationToken).ConfigureAwait(false);
        try { await action(cancellationToken).ConfigureAwait(false); }
        finally { await store.OpenAsync(key, create: false, cancellationToken).ConfigureAwait(false); }
    }

    public async ValueTask DisposeAsync()
    {
        await LockAsync().ConfigureAwait(false);
    }

    private async Task<VaultMetadata?> ReadMetadataAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using FileStream stream = File.OpenRead(paths.MetadataPath);
            return await JsonSerializer.DeserializeAsync(stream, VaultMetadataJsonContext.Default.VaultMetadata, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException) { return null; }
    }

    private async Task WriteMetadataAtomicAsync(VaultMetadata metadata, CancellationToken cancellationToken)
    {
        string temporaryPath = paths.MetadataPath + ".tmp";
        await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, metadata, VaultMetadataJsonContext.Default.VaultMetadata, cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporaryPath, paths.MetadataPath, overwrite: true);
    }

    private static void WriteUnlockTiming(long startedAt, long metadataReadAt, long keyUnwrappedAt, long? databaseOpenedAt)
    {
        double metadataMilliseconds = Stopwatch.GetElapsedTime(startedAt, metadataReadAt).TotalMilliseconds;
        double keyDerivationMilliseconds = Stopwatch.GetElapsedTime(metadataReadAt, keyUnwrappedAt).TotalMilliseconds;
        string databaseTiming = databaseOpenedAt is { } openedAt
            ? $", database open and migration={Stopwatch.GetElapsedTime(keyUnwrappedAt, openedAt).TotalMilliseconds:F0} ms"
            : string.Empty;
        Debug.WriteLine($"SummonersVault unlock timing: metadata={metadataMilliseconds:F0} ms, key derivation={keyDerivationMilliseconds:F0} ms{databaseTiming}");
    }
}
