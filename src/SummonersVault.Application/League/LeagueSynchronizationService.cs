using SummonersVault.Application.Abstractions;
using SummonersVault.Core.Models;

namespace SummonersVault.Application.League;

public sealed class LeagueSynchronizationService(
    ILeagueClientGateway leagueClient,
    IAccountRepository accountRepository)
{
    public async Task<LeagueSnapshot> SynchronizeAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        LeagueSnapshot snapshot = await leagueClient.FetchCurrentSnapshotAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<LeagueAccountIdentity> accounts = await accountRepository.GetLeagueAccountIdentitiesAsync(cancellationToken).ConfigureAwait(false);
        ValidateIdentity(accounts, accountId, snapshot.Puuid, snapshot.RiotGameName, snapshot.RiotTagLine);

        await accountRepository.ApplyLeagueSnapshotAsync(accountId, snapshot, cancellationToken).ConfigureAwait(false);
        return snapshot;
    }

    public async Task<ChampionProgressionSnapshot> SynchronizeChampionProgressionAsync(
        Guid accountId,
        string expectedPuuid,
        CancellationToken cancellationToken = default)
    {
        ChampionProgressionSnapshot snapshot = await leagueClient.FetchChampionProgressionAsync(cancellationToken).ConfigureAwait(false);
        if (!string.Equals(snapshot.Puuid, expectedPuuid, StringComparison.Ordinal))
        {
            throw new LeagueIdentityConflictException("The signed-in League account changed before champion progression could be synchronized.");
        }

        IReadOnlyList<LeagueAccountIdentity> accounts = await accountRepository.GetLeagueAccountIdentitiesAsync(cancellationToken).ConfigureAwait(false);
        ValidateIdentity(accounts, accountId, snapshot.Puuid, null, null);
        await accountRepository.ApplyChampionProgressionAsync(accountId, snapshot, cancellationToken).ConfigureAwait(false);
        return snapshot;
    }

    private static void ValidateIdentity(
        IReadOnlyList<LeagueAccountIdentity> accounts,
        Guid accountId,
        string puuid,
        string? riotGameName,
        string? riotTagLine)
    {
        LeagueAccountIdentity target = accounts.FirstOrDefault(account => account.Id == accountId)
            ?? throw new InvalidOperationException("The selected vault account no longer exists.");

        if (!string.IsNullOrWhiteSpace(target.Puuid)
            && !string.Equals(target.Puuid, puuid, StringComparison.Ordinal))
        {
            string signedInRiotId = FormatRiotId(riotGameName, riotTagLine, "the current League profile");
            string linkedRiotId = FormatRiotId(target.RiotGameName, target.RiotTagLine, "another League profile");
            throw new LeagueIdentityConflictException($"The signed-in League account ({signedInRiotId}) does not match {target.DisplayName}, which is linked to {linkedRiotId}. Sign in to the matching League account and try again.");
        }

        LeagueAccountIdentity? existing = accounts.FirstOrDefault(account =>
            string.Equals(account.Puuid, puuid, StringComparison.Ordinal)
            && account.Id != accountId);
        if (existing is not null)
        {
            throw new LeagueIdentityConflictException($"That League profile is already linked to {existing.DisplayName}.");
        }
    }

    private static string FormatRiotId(string? gameName, string? tagLine, string fallback) =>
        string.IsNullOrWhiteSpace(gameName)
            ? fallback
            : string.IsNullOrWhiteSpace(tagLine) ? gameName : $"{gameName}#{tagLine}";
}

public sealed class LeagueIdentityConflictException(string message) : InvalidOperationException(message);
