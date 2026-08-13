using SummonersVault.Application.Abstractions;
using SummonersVault.Application.Vault;
using Xunit;

namespace SummonersVault.Tests;

public sealed class VaultServiceTests
{
    [Fact]
    public async Task ConcurrentUnlocks_AuthenticateOnlyOnceAndBothObserveSuccess()
    {
        var session = new BlockingVaultSession();
        var service = new VaultService(session);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        Task<bool> firstUnlock = service.UnlockAsync("correct password"u8.ToArray(), timeout.Token);
        await session.UnlockStarted.Task.WaitAsync(timeout.Token);
        Task<bool> repeatedUnlock = service.UnlockAsync(ReadOnlyMemory<byte>.Empty, timeout.Token);

        session.CompleteUnlock(success: true);

        Assert.True(await firstUnlock.WaitAsync(timeout.Token));
        Assert.True(await repeatedUnlock.WaitAsync(timeout.Token));
        Assert.Equal(1, session.UnlockCount);
    }

    [Fact]
    public async Task AlreadyUnlockedSession_DoesNotAuthenticateAgain()
    {
        var session = new BlockingVaultSession { IsUnlocked = true };
        var service = new VaultService(session);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        bool result = await service.UnlockAsync(ReadOnlyMemory<byte>.Empty, timeout.Token);

        Assert.True(result);
        Assert.Equal(0, session.UnlockCount);
    }

    private sealed class BlockingVaultSession : IVaultSession
    {
        private readonly TaskCompletionSource<bool> _unlockCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource UnlockStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Exists => true;
        public bool IsUnlocked { get; set; }
        public int UnlockCount { get; private set; }

        public void CompleteUnlock(bool success) => _unlockCompletion.TrySetResult(success);

        public Task CreateAsync(ReadOnlyMemory<byte> masterPasswordUtf8, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async Task<bool> UnlockAsync(ReadOnlyMemory<byte> masterPasswordUtf8, CancellationToken cancellationToken = default)
        {
            UnlockCount++;
            UnlockStarted.TrySetResult();
            bool result = await _unlockCompletion.Task.WaitAsync(cancellationToken);
            IsUnlocked = result;
            return result;
        }

        public Task ChangeMasterPasswordAsync(ReadOnlyMemory<byte> currentPasswordUtf8, ReadOnlyMemory<byte> newPasswordUtf8, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task LockAsync(CancellationToken cancellationToken = default)
        {
            IsUnlocked = false;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
