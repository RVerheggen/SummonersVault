using SummonersVault.Core.Models;

namespace SummonersVault.Core.Abstractions;

public interface IVaultRepository : IAsyncDisposable
{
    bool IsOpen { get; }
    string DatabasePath { get; }
    Task OpenAsync(ReadOnlyMemory<byte> databaseKey, bool create, CancellationToken cancellationToken = default);
    Task CloseAsync();
    Task<IReadOnlyList<VaultAccount>> GetAccountsAsync(CancellationToken cancellationToken = default);
    Task<VaultAccount?> GetAccountAsync(Guid id, bool includePassword = false, CancellationToken cancellationToken = default);
    Task SaveAccountAsync(VaultAccount account, CancellationToken cancellationToken = default);
    Task DeleteAccountAsync(Guid id, CancellationToken cancellationToken = default);
    Task ApplyLeagueSnapshotAsync(Guid accountId, LeagueSnapshot snapshot, CancellationToken cancellationToken = default);
}

public interface IVaultSession : IAsyncDisposable
{
    bool Exists { get; }
    bool IsUnlocked { get; }
    IVaultRepository Repository { get; }
    Task CreateAsync(ReadOnlyMemory<byte> masterPasswordUtf8, CancellationToken cancellationToken = default);
    Task<bool> UnlockAsync(ReadOnlyMemory<byte> masterPasswordUtf8, CancellationToken cancellationToken = default);
    Task ChangeMasterPasswordAsync(ReadOnlyMemory<byte> currentPasswordUtf8, ReadOnlyMemory<byte> newPasswordUtf8, CancellationToken cancellationToken = default);
    Task LockAsync();
}

public interface ILeagueClientGateway
{
    Task<LeagueClientStatus> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<LeagueSnapshot> FetchCurrentSnapshotAsync(CancellationToken cancellationToken = default);
    Task<bool> LaunchAsync(string? configuredInstallDirectory, CancellationToken cancellationToken = default);
    Task<byte[]?> FetchAssetAsync(string assetPath, CancellationToken cancellationToken = default);
}

public interface IArtworkService
{
    Task<string?> ResolveAsync(string? assetPath, bool allowCommunityDragon, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
    long GetCacheSizeBytes();
}

public interface IBackupService
{
    Task ExportAsync(string destinationPath, CancellationToken cancellationToken = default);
    Task<BackupImportPreview> PreviewImportAsync(string backupPath, ReadOnlyMemory<byte> sourceMasterPasswordUtf8, CancellationToken cancellationToken = default);
    Task ImportAsync(BackupImportPreview preview, IReadOnlyDictionary<Guid, BackupConflictChoice> choices, CancellationToken cancellationToken = default);
}

public enum BackupConflictChoice { KeepCurrent, UseImported }
public sealed record BackupConflict(Guid ImportedId, Guid CurrentId, string DisplayName);
public sealed record BackupImportPreview(string TemporaryDirectory, IReadOnlyList<VaultAccount> NewAccounts, IReadOnlyList<BackupConflict> Conflicts) : IAsyncDisposable
{
    public ValueTask DisposeAsync()
    {
        foreach (var account in NewAccounts)
            if (account.PasswordUtf8.Length > 0) System.Security.Cryptography.CryptographicOperations.ZeroMemory(account.PasswordUtf8);
        if (Directory.Exists(TemporaryDirectory)) Directory.Delete(TemporaryDirectory, true);
        return ValueTask.CompletedTask;
    }
}
