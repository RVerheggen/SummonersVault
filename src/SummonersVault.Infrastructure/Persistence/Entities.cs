namespace SummonersVault.Infrastructure.Persistence;

internal sealed class AccountEntity
{
    public Guid Id { get; set; }
    public string LoginIdentifier { get; set; } = string.Empty;
    public byte[] Password { get; set; } = [];
    public string? Label { get; set; }
    public string Region { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public int Roles { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ModifiedAtUtc { get; set; }
    public string? Puuid { get; set; }
    public long? SummonerId { get; set; }
    public string? RiotGameName { get; set; }
    public string? RiotTagLine { get; set; }
    public int? ProfileIconId { get; set; }
    public byte[]? ProfileIcon { get; set; }
    public int? SummonerLevel { get; set; }
    public DateTimeOffset? LastSyncedAtUtc { get; set; }
    public DateTimeOffset? LastMatchPlayedAtUtc { get; set; }
    public long? LastMatchId { get; set; }
    public DateTimeOffset? MatchHistorySyncedAtUtc { get; set; }
    public int MatchHistoryState { get; set; }
    public long? RiotPoints { get; set; }
    public long? BlueEssence { get; set; }
    public List<RankEntity> Ranks { get; set; } = [];
    public List<ChampionEntity> Champions { get; set; } = [];
    public List<ChampionMasteryEntity> ChampionMasteries { get; set; } = [];
    public List<EternalSummaryEntity> EternalSummaries { get; set; } = [];
    public List<EternalSetEntity> EternalSets { get; set; } = [];
    public List<EternalEntity> Eternals { get; set; } = [];
    public List<SkinEntity> Skins { get; set; } = [];
    public List<LootItemEntity> LootItems { get; set; } = [];
    public List<SyncCategoryEntity> SyncCategories { get; set; } = [];
}

internal sealed class RankEntity
{
    public Guid AccountId { get; set; }
    public string QueueType { get; set; } = string.Empty;
    public string Tier { get; set; } = string.Empty;
    public string Division { get; set; } = string.Empty;
    public int LeaguePoints { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public bool IsProvisional { get; set; }
    public int? ProvisionalGamesRemaining { get; set; }
    public string? RatedTier { get; set; }
    public int? RatedRating { get; set; }
    public AccountEntity Account { get; set; } = null!;
}

internal sealed class ChampionEntity
{
    public Guid AccountId { get; set; }
    public int ChampionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? BaseSplashPath { get; set; }
    public string? SquarePortraitPath { get; set; }
    public string? Alias { get; set; }
    public int Variant { get; set; }
    public AccountEntity Account { get; set; } = null!;
}

internal sealed class ChampionMasteryEntity
{
    public Guid AccountId { get; set; }
    public int ChampionId { get; set; }
    public int Level { get; set; }
    public long Points { get; set; }
    public long PointsSinceLastLevel { get; set; }
    public long PointsUntilNextLevel { get; set; }
    public int SeasonMilestone { get; set; }
    public string? HighestGrade { get; set; }
    public DateTimeOffset? LastPlayAtUtc { get; set; }
    public int MarksRequiredForNextLevel { get; set; }
    public string MilestoneGradesJson { get; set; } = "[]";
    public int TokensEarned { get; set; }
    public AccountEntity Account { get; set; } = null!;
}

internal sealed class EternalSummaryEntity
{
    public Guid AccountId { get; set; }
    public int ChampionId { get; set; }
    public int MilestonesPassed { get; set; }
    public int StonesAvailable { get; set; }
    public int StonesIlluminated { get; set; }
    public int StonesOwned { get; set; }
    public AccountEntity Account { get; set; } = null!;
}

internal sealed class EternalSetEntity
{
    public Guid AccountId { get; set; }
    public int ChampionId { get; set; }
    public int SetId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int MilestonesPassed { get; set; }
    public int StonesAvailable { get; set; }
    public int StonesIlluminated { get; set; }
    public int StonesOwned { get; set; }
    public AccountEntity Account { get; set; } = null!;
}

internal sealed class EternalEntity
{
    public Guid AccountId { get; set; }
    public string StatstoneId { get; set; } = string.Empty;
    public int ChampionId { get; set; }
    public int SetId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Category { get; set; }
    public double Value { get; set; }
    public string? FormattedValue { get; set; }
    public int MilestoneLevel { get; set; }
    public string? FormattedMilestoneLevel { get; set; }
    public double? NextMilestone { get; set; }
    public double? PersonalBest { get; set; }
    public string? FormattedPersonalBest { get; set; }
    public bool IsComplete { get; set; }
    public bool IsEpic { get; set; }
    public bool IsFeatured { get; set; }
    public bool IsRetired { get; set; }
    public string? ImageAssetPath { get; set; }
    public AccountEntity Account { get; set; } = null!;
}

internal sealed class SkinEntity
{
    public Guid AccountId { get; set; }
    public int SkinId { get; set; }
    public int ChampionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? SplashPath { get; set; }
    public string? TilePath { get; set; }
    public AccountEntity Account { get; set; } = null!;
}

internal sealed class LootItemEntity
{
    public Guid AccountId { get; set; }
    public string LootId { get; set; } = string.Empty;
    public string LootName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string DisplayCategory { get; set; } = string.Empty;
    public string LocalizedName { get; set; } = string.Empty;
    public string? LocalizedDescription { get; set; }
    public int Count { get; set; }
    public string? Rarity { get; set; }
    public string? ReferenceId { get; set; }
    public string? AssetPath { get; set; }
    public string? SplashPath { get; set; }
    public string? TilePath { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public int? DisenchantValue { get; set; }
    public int? UpgradeEssenceValue { get; set; }
    public AccountEntity Account { get; set; } = null!;
}

internal sealed class SyncCategoryEntity
{
    public Guid AccountId { get; set; }
    public int Category { get; set; }
    public int State { get; set; }
    public DateTimeOffset? LastAttemptAtUtc { get; set; }
    public DateTimeOffset? LastSuccessAtUtc { get; set; }
    public AccountEntity Account { get; set; } = null!;
}
