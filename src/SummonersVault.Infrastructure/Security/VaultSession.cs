using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SummonersVault.Core.Abstractions;
using SummonersVault.Infrastructure.Storage;

namespace SummonersVault.Infrastructure.Security;

public sealed class VaultSession(VaultPaths paths, IVaultRepository repository) : IVaultSession
{
    private byte[]? _databaseKey;
    public bool Exists => File.Exists(paths.DatabasePath) && File.Exists(paths.MetadataPath);
    public bool IsUnlocked => _databaseKey is not null && repository.IsOpen;
    public IVaultRepository Repository => repository;

    public async Task CreateAsync(ReadOnlyMemory<byte> masterPasswordUtf8, CancellationToken cancellationToken = default)
    {
        if (Exists) throw new InvalidOperationException("A vault already exists.");
        VaultKeyEnvelope.ValidateMasterPassword(masterPasswordUtf8.Span);
        paths.EnsureCreated();
        var databaseKey = RandomNumberGenerator.GetBytes(32);
        try
        {
            await repository.OpenAsync(databaseKey, create: true, cancellationToken).ConfigureAwait(false);
            var metadata = VaultKeyEnvelope.Create(Guid.NewGuid(), masterPasswordUtf8.Span, databaseKey);
            await WriteMetadataAtomicAsync(metadata, cancellationToken).ConfigureAwait(false);
            _databaseKey = databaseKey;
            databaseKey = [];
        }
        finally { CryptographicOperations.ZeroMemory(databaseKey); }
    }

    public async Task<bool> UnlockAsync(ReadOnlyMemory<byte> masterPasswordUtf8, CancellationToken cancellationToken = default)
    {
        if (!Exists) return false;
        var metadata = await ReadMetadataAsync(cancellationToken).ConfigureAwait(false);
        if (metadata is null || !VaultKeyEnvelope.TryUnwrap(metadata, masterPasswordUtf8.Span, out var databaseKey)) return false;
        try
        {
            await repository.OpenAsync(databaseKey, create: false, cancellationToken).ConfigureAwait(false);
            _databaseKey = databaseKey;
            databaseKey = [];
            return true;
        }
        catch (SqliteException)
        {
            await repository.CloseAsync().ConfigureAwait(false);
            return false;
        }
        finally { CryptographicOperations.ZeroMemory(databaseKey); }
    }

    public async Task ChangeMasterPasswordAsync(ReadOnlyMemory<byte> currentPasswordUtf8, ReadOnlyMemory<byte> newPasswordUtf8, CancellationToken cancellationToken = default)
    {
        if (_databaseKey is null) throw new InvalidOperationException("Unlock the vault first.");
        VaultKeyEnvelope.ValidateMasterPassword(newPasswordUtf8.Span);
        var metadata = await ReadMetadataAsync(cancellationToken).ConfigureAwait(false) ?? throw new InvalidDataException("Vault metadata is invalid.");
        if (!VaultKeyEnvelope.TryUnwrap(metadata, currentPasswordUtf8.Span, out var verifiedKey)) throw new UnauthorizedAccessException("The current master password is incorrect.");
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(verifiedKey, _databaseKey)) throw new CryptographicException("Vault key verification failed.");
            var replacement = VaultKeyEnvelope.Create(metadata.VaultId, newPasswordUtf8.Span, _databaseKey);
            await WriteMetadataAtomicAsync(replacement, cancellationToken).ConfigureAwait(false);
        }
        finally { CryptographicOperations.ZeroMemory(verifiedKey); }
    }

    public async Task LockAsync()
    {
        await repository.CloseAsync().ConfigureAwait(false);
        if (_databaseKey is not null)
        {
            CryptographicOperations.ZeroMemory(_databaseKey);
            _databaseKey = null;
        }
    }

    internal ReadOnlyMemory<byte> RequireDatabaseKey() => _databaseKey ?? throw new InvalidOperationException("The vault is locked.");

    internal async Task WithClosedRepositoryAsync(Func<Task> action, CancellationToken cancellationToken = default)
    {
        var key = _databaseKey ?? throw new InvalidOperationException("The vault is locked.");
        await repository.CloseAsync().ConfigureAwait(false);
        try { await action().ConfigureAwait(false); }
        finally { await repository.OpenAsync(key, create: false, cancellationToken).ConfigureAwait(false); }
    }

    public async ValueTask DisposeAsync()
    {
        await LockAsync().ConfigureAwait(false);
        await repository.DisposeAsync().ConfigureAwait(false);
    }

    private async Task<VaultMetadata?> ReadMetadataAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(paths.MetadataPath);
            return await JsonSerializer.DeserializeAsync(stream, VaultMetadataJsonContext.Default.VaultMetadata, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException) { return null; }
    }

    private async Task WriteMetadataAtomicAsync(VaultMetadata metadata, CancellationToken cancellationToken)
    {
        var temporaryPath = paths.MetadataPath + ".tmp";
        await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            await JsonSerializer.SerializeAsync(stream, metadata, VaultMetadataJsonContext.Default.VaultMetadata, cancellationToken).ConfigureAwait(false);
        File.Move(temporaryPath, paths.MetadataPath, overwrite: true);
    }
}
