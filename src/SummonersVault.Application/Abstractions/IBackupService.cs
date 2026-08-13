using SummonersVault.Application.Accounts;

namespace SummonersVault.Application.Abstractions;

public interface IBackupService
{
    Task ExportAsync(string destinationPath, CancellationToken cancellationToken = default);
    Task<BackupImportPreview> PreviewImportAsync(string backupPath, ReadOnlyMemory<byte> sourceMasterPasswordUtf8, CancellationToken cancellationToken = default);
    Task ImportAsync(BackupImportPreview preview, IReadOnlyDictionary<Guid, BackupConflictChoice> choices, CancellationToken cancellationToken = default);
}

public enum BackupConflictChoice
{
    KeepCurrent,
    UseImported
}

public sealed record BackupConflict(Guid ImportedId, Guid CurrentId, string DisplayName);

public sealed record BackupImportPreview(
    string TemporaryDirectory,
    IReadOnlyList<AccountImportItem> NewAccounts,
    IReadOnlyList<BackupConflict> Conflicts) : IAsyncDisposable
{
    public ValueTask DisposeAsync()
    {
        foreach (AccountImportItem item in NewAccounts)
        {
            item.Password.Dispose();
        }

        if (Directory.Exists(TemporaryDirectory))
        {
            Directory.Delete(TemporaryDirectory, true);
        }

        return ValueTask.CompletedTask;
    }
}
