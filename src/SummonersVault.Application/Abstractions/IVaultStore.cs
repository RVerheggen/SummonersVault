namespace SummonersVault.Application.Abstractions;

public interface IVaultStore : IAsyncDisposable
{
    bool IsOpen { get; }
    string DatabasePath { get; }
    Task OpenAsync(ReadOnlyMemory<byte> databaseKey, bool create, CancellationToken cancellationToken = default);
    Task CloseAsync(CancellationToken cancellationToken = default);
}
