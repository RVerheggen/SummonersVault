using SummonersVault.Application.Abstractions;
using SummonersVault.Application.Security;
using SummonersVault.Core.Models;

namespace SummonersVault.Application.Accounts;

public sealed class AccountService(IAccountRepository repository)
{
    public Task<IReadOnlyList<VaultAccount>> GetAllAsync(CancellationToken cancellationToken = default) =>
        repository.GetAccountsAsync(cancellationToken);

    public Task<VaultAccount?> GetAsync(Guid accountId, CancellationToken cancellationToken = default) =>
        repository.GetAccountAsync(accountId, cancellationToken);

    public Task<SensitiveBuffer?> GetPasswordAsync(Guid accountId, CancellationToken cancellationToken = default) =>
        repository.GetPasswordAsync(accountId, cancellationToken);

    public async Task SaveAsync(AccountSaveRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Account.ModifiedAtUtc = DateTimeOffset.UtcNow;
        await repository.SaveAccountAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public Task DeleteAsync(Guid accountId, CancellationToken cancellationToken = default) =>
        repository.DeleteAccountAsync(accountId, cancellationToken);
}
