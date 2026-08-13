using SummonersVault.Application.Abstractions;
using SummonersVault.Core.Models;
using SummonersVault.Core.Services;

namespace SummonersVault.Application.League;

public sealed class LeagueSynchronizationService(
    ILeagueClientGateway leagueClient,
    IAccountRepository accountRepository)
{
    public async Task<LeagueSnapshot> SynchronizeAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        LeagueSnapshot snapshot = await leagueClient.FetchCurrentSnapshotAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<VaultAccount> accounts = await accountRepository.GetAccountsAsync(cancellationToken).ConfigureAwait(false);
        VaultAccount target = accounts.FirstOrDefault(account => account.Id == accountId)
            ?? throw new InvalidOperationException("The selected vault account no longer exists.");

        if (!LeagueIdentityRules.MatchesLinkedAccount(target, snapshot.Puuid))
        {
            string signedInRiotId = FormatRiotId(snapshot.RiotGameName, snapshot.RiotTagLine, "the current League profile");
            string linkedRiotId = FormatRiotId(target.RiotGameName, target.RiotTagLine, "another League profile");
            throw new LeagueIdentityConflictException($"The signed-in League account ({signedInRiotId}) does not match {target.DisplayName}, which is linked to {linkedRiotId}. Sign in to the matching League account and try again.");
        }

        VaultAccount? existing = accounts.FirstOrDefault(account =>
            string.Equals(account.Puuid, snapshot.Puuid, StringComparison.Ordinal)
            && account.Id != accountId);
        if (existing is not null)
        {
            throw new LeagueIdentityConflictException($"That League profile is already linked to {existing.DisplayName}.");
        }

        await accountRepository.ApplyLeagueSnapshotAsync(accountId, snapshot, cancellationToken).ConfigureAwait(false);
        return snapshot;
    }

    private static string FormatRiotId(string? gameName, string? tagLine, string fallback) =>
        string.IsNullOrWhiteSpace(gameName)
            ? fallback
            : string.IsNullOrWhiteSpace(tagLine) ? gameName : $"{gameName}#{tagLine}";
}

public sealed class LeagueIdentityConflictException(string message) : InvalidOperationException(message);
