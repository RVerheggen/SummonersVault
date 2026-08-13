using SummonersVault.Application.Accounts;
using SummonersVault.Application.Security;
using SummonersVault.Core.Models;

namespace SummonersVault.Application.Abstractions;

public interface IAccountRepository
{
    Task<IReadOnlyList<VaultAccount>> GetAccountsAsync(CancellationToken cancellationToken = default);
    Task<VaultAccount?> GetAccountAsync(Guid accountId, CancellationToken cancellationToken = default);
    Task<SensitiveBuffer?> GetPasswordAsync(Guid accountId, CancellationToken cancellationToken = default);
    Task SaveAccountAsync(AccountSaveRequest request, CancellationToken cancellationToken = default);
    Task DeleteAccountAsync(Guid accountId, CancellationToken cancellationToken = default);
    Task ApplyLeagueSnapshotAsync(Guid accountId, LeagueSnapshot snapshot, CancellationToken cancellationToken = default);
    Task MergeAccountsAsync(IReadOnlyList<AccountImportItem> accounts, CancellationToken cancellationToken = default);
}
