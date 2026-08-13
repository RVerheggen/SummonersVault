namespace SummonersVault.Application.Abstractions;

public interface IVaultSession : IAsyncDisposable
{
    bool Exists { get; }
    bool IsUnlocked { get; }
    Task CreateAsync(ReadOnlyMemory<byte> masterPasswordUtf8, CancellationToken cancellationToken = default);
    Task<bool> UnlockAsync(ReadOnlyMemory<byte> masterPasswordUtf8, CancellationToken cancellationToken = default);
    Task ChangeMasterPasswordAsync(ReadOnlyMemory<byte> currentPasswordUtf8, ReadOnlyMemory<byte> newPasswordUtf8, CancellationToken cancellationToken = default);
    Task LockAsync(CancellationToken cancellationToken = default);
}
