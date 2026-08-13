namespace SummonersVault.Application.Abstractions;

public interface IVaultFileAccess
{
    Task WithVaultClosedAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken = default);
}
