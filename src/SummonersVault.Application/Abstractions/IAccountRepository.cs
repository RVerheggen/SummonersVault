using SummonersVault.Application.Accounts;
using SummonersVault.Application.Security;
using SummonersVault.Core.Models;

namespace SummonersVault.Application.Abstractions;

public interface IAccountRepository
{
    Task<IReadOnlyList<VaultAccount>> GetAccountsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LeagueAccountIdentity>> GetLeagueAccountIdentitiesAsync(CancellationToken cancellationToken = default);
    Task<VaultAccount?> GetAccountAsync(Guid accountId, CancellationToken cancellationToken = default);
    Task<SensitiveBuffer?> GetPasswordAsync(Guid accountId, CancellationToken cancellationToken = default);
    Task SaveAccountAsync(AccountSaveRequest request, CancellationToken cancellationToken = default);
    Task DeleteAccountAsync(Guid accountId, CancellationToken cancellationToken = default);
    Task ApplyLeagueSnapshotAsync(Guid accountId, LeagueSnapshot snapshot, CancellationToken cancellationToken = default);
    Task ApplyChampionProgressionAsync(Guid accountId, ChampionProgressionSnapshot snapshot, CancellationToken cancellationToken = default);
    Task MergeAccountsAsync(IReadOnlyList<AccountImportItem> accounts, CancellationToken cancellationToken = default);
}

public sealed record LeagueAccountIdentity(
    Guid Id,
    string Username,
    string? Label,
    string? Puuid,
    string? RiotGameName,
    string? RiotTagLine)
{
    public string DisplayName => !string.IsNullOrWhiteSpace(Label)
        ? Label
        : !string.IsNullOrWhiteSpace(RiotGameName)
            ? $"{RiotGameName}#{RiotTagLine}"
            : Username;
}
