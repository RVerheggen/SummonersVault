using SummonersVault.Application.Abstractions;

namespace SummonersVault.Application.Backup;

public sealed class BackupService(IBackupService backupService)
{
    public Task ExportAsync(string destinationPath, CancellationToken cancellationToken = default) =>
        backupService.ExportAsync(destinationPath, cancellationToken);

    public Task<BackupImportPreview> PreviewImportAsync(
        string backupPath,
        ReadOnlyMemory<byte> sourceMasterPasswordUtf8,
        CancellationToken cancellationToken = default) =>
        backupService.PreviewImportAsync(backupPath, sourceMasterPasswordUtf8, cancellationToken);

    public Task ImportAsync(
        BackupImportPreview preview,
        IReadOnlyDictionary<Guid, BackupConflictChoice> choices,
        CancellationToken cancellationToken = default) =>
        backupService.ImportAsync(preview, choices, cancellationToken);
}
