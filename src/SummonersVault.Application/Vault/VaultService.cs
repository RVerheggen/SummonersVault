using SummonersVault.Application.Abstractions;

namespace SummonersVault.Application.Vault;

public sealed class VaultService(IVaultSession session)
{
    private readonly SemaphoreSlim _unlockGate = new(1, 1);

    public bool Exists => session.Exists;
    public bool IsUnlocked => session.IsUnlocked;

    public Task CreateAsync(ReadOnlyMemory<byte> masterPasswordUtf8, CancellationToken cancellationToken = default) =>
        session.CreateAsync(masterPasswordUtf8, cancellationToken);

    public async Task<bool> UnlockAsync(ReadOnlyMemory<byte> masterPasswordUtf8, CancellationToken cancellationToken = default)
    {
        await _unlockGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return session.IsUnlocked || await session.UnlockAsync(masterPasswordUtf8, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _unlockGate.Release();
        }
    }

    public Task ChangeMasterPasswordAsync(ReadOnlyMemory<byte> currentPasswordUtf8, ReadOnlyMemory<byte> newPasswordUtf8, CancellationToken cancellationToken = default) =>
        session.ChangeMasterPasswordAsync(currentPasswordUtf8, newPasswordUtf8, cancellationToken);

    public Task LockAsync(CancellationToken cancellationToken = default) => session.LockAsync(cancellationToken);
}
