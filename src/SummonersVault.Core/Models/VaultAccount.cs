namespace SummonersVault.Core.Models;

[Flags]
public enum AccountRole
{
    None = 0,
    Top = 1,
    Jungle = 2,
    Mid = 4,
    Bot = 8,
    Support = 16
}

public enum MatchHistoryState
{
    Unknown,
    NeverPlayed,
    Known,
    Stale
}

public enum SnapshotCategory { Ranked, Wallet, Champions, Skins, Crafting }
public enum SnapshotState { Unknown, Current, Stale }

public sealed record SnapshotCategoryStatus(
    SnapshotCategory Category,
    SnapshotState State,
    DateTimeOffset? LastAttemptAtUtc,
    DateTimeOffset? LastSuccessAtUtc);

public sealed class VaultAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string LoginIdentifier { get; set; } = string.Empty;
    public byte[] PasswordUtf8 { get; set; } = [];
    public string? Label { get; set; }
    public string Region { get; set; } = "EUW";
    public string? Notes { get; set; }
    public AccountRole Roles { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string? Puuid { get; set; }
    public long? SummonerId { get; set; }
    public string? RiotGameName { get; set; }
    public string? RiotTagLine { get; set; }
    public int? ProfileIconId { get; set; }
    public byte[]? ProfileIconBytes { get; set; }
    public int? SummonerLevel { get; set; }
    public long? RiotPoints { get; set; }
    public long? BlueEssence { get; set; }
    public DateTimeOffset? LastSyncedAtUtc { get; set; }
    public DateTimeOffset? LastMatchPlayedAtUtc { get; set; }
    public long? LastMatchId { get; set; }
    public DateTimeOffset? MatchHistorySyncedAtUtc { get; set; }
    public MatchHistoryState MatchHistoryState { get; set; }
    public List<RankSnapshot> Ranks { get; } = [];
    public List<OwnedChampion> Champions { get; } = [];
    public List<OwnedSkin> Skins { get; } = [];
    public List<CraftingLootItem> LootItems { get; } = [];
    public List<SnapshotCategoryStatus> SyncCategories { get; } = [];

    public string DisplayName => !string.IsNullOrWhiteSpace(Label)
        ? Label
        : !string.IsNullOrWhiteSpace(RiotGameName)
            ? $"{RiotGameName}#{RiotTagLine}"
            : LoginIdentifier;

    public RankSnapshot? CardRank => Ranks.FirstOrDefault(x => x.QueueType == "RANKED_SOLO_5x5")
        ?? Ranks.FirstOrDefault(x => x.QueueType == "RANKED_FLEX_SR");

    public VaultAccount CloneWithoutPassword()
    {
        var clone = (VaultAccount)MemberwiseClone();
        clone.PasswordUtf8 = [];
        return clone;
    }
}

public sealed record RankSnapshot(
    string QueueType,
    string Tier,
    string Division,
    int LeaguePoints,
    int Wins,
    int Losses,
    bool IsProvisional = false,
    int? ProvisionalGamesRemaining = null,
    string? RatedTier = null,
    int? RatedRating = null);

public sealed record OwnedChampion(
    int ChampionId,
    string Name,
    string? BaseSplashAssetPath = null,
    string? SquarePortraitAssetPath = null);

public sealed record OwnedSkin(
    int SkinId,
    int ChampionId,
    string Name,
    string? SplashAssetPath = null,
    string? TileAssetPath = null);

public sealed record CraftingLootItem(
    string LootId,
    string LootName,
    string Type,
    string DisplayCategory,
    string LocalizedName,
    string? LocalizedDescription,
    int Count,
    string? Rarity,
    string? ReferenceId,
    string? AssetPath,
    string? SplashAssetPath,
    string? TileAssetPath,
    DateTimeOffset? ExpiresAtUtc,
    int? DisenchantValue,
    int? UpgradeEssenceValue);
