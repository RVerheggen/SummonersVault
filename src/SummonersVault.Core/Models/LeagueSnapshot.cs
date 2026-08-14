namespace SummonersVault.Core.Models;

public sealed class LeagueSnapshot
{
    public required string Puuid { get; init; }
    public long? SummonerId { get; init; }
    public required string RiotGameName { get; init; }
    public required string RiotTagLine { get; init; }
    public required string Region { get; init; }
    public int? ProfileIconId { get; init; }
    public byte[]? ProfileIconBytes { get; init; }
    public int? SummonerLevel { get; init; }
    public LeagueWalletSnapshot? Wallet { get; init; }
    public IReadOnlyList<RankSnapshot>? Ranks { get; init; }
    public IReadOnlyList<OwnedChampion>? Champions { get; init; }
    public IReadOnlyList<ChampionMastery>? ChampionMasteries { get; init; }
    public ChampionEternalsSnapshot? ChampionEternals { get; init; }
    public IReadOnlyList<OwnedSkin>? Skins { get; init; }
    public IReadOnlyList<CraftingLootItem>? CraftingLoot { get; init; }
    public MatchSnapshotResult Match { get; init; } = MatchSnapshotResult.Failed;
    public bool HasCompleteInventory => Champions is not null && Skins is not null;
    public bool HasCompleteSyncData => HasCompleteInventory && Ranks is not null && CraftingLoot is not null
        && Wallet is { RiotPoints: not null, BlueEssence: not null };
}

public sealed class ChampionProgressionSnapshot
{
    public required string Puuid { get; init; }
    public IReadOnlyList<ChampionMastery>? ChampionMasteries { get; init; }
    public ChampionEternalsSnapshot? ChampionEternals { get; init; }
    public bool IsComplete => ChampionMasteries is not null && ChampionEternals?.IsComplete == true;
}

public sealed record ChampionEternalsSnapshot(
    IReadOnlyList<ChampionEternalSummary> Summaries,
    IReadOnlyList<ChampionEternalSet> Sets,
    IReadOnlyList<ChampionEternal> Eternals,
    IReadOnlySet<int> SuccessfullyLoadedChampionIds,
    bool IsComplete);

public sealed record MatchSnapshotResult(bool Succeeded, bool HasMatch, DateTimeOffset? PlayedAtUtc, long? MatchId)
{
    public static MatchSnapshotResult Failed { get; } = new(false, false, null, null);
    public static MatchSnapshotResult Empty { get; } = new(true, false, null, null);
    public static MatchSnapshotResult Known(DateTimeOffset playedAtUtc, long? matchId) => new(true, true, playedAtUtc, matchId);
}

public sealed record LeagueClientStatus(bool IsRunning, bool IsLoggedIn, string Message);
public sealed record LeagueWalletSnapshot(long? RiotPoints, long? BlueEssence);
