using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using SummonersVault.Application.Abstractions;
using SummonersVault.Application.ExternalProfiles;
using SummonersVault.Application.Settings;
using SummonersVault.Core.Models;

namespace SummonersVault.App.ViewModels;

public sealed partial class AccountDetailsViewModel : ObservableObject, IDisposable
{
    private readonly VaultAccount _account;
    private AppSettings _settings;
    private CancellationTokenSource? _debounce;
    [ObservableProperty]
    public partial string ChampionQuery { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ChampionCollection { get; set; } = "Current champions";

    [ObservableProperty]
    public partial string ChampionSort { get; set; } = "Name";

    [ObservableProperty]
    public partial string ChampionSortDirection { get; set; } = "Ascending";

    [ObservableProperty]
    public partial int GalleryColumnCount { get; set; } = 3;

    [ObservableProperty]
    public partial int CraftingColumnCount { get; set; } = 3;

    [ObservableProperty]
    public partial ChampionGalleryItem? SelectedChampion { get; set; }

    [ObservableProperty]
    public partial string SkinQuery { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LootQuery { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LootCategory { get; set; } = "All";

    [ObservableProperty]
    public partial string SynchronizationStatus { get; set; } = string.Empty;

    public AccountDetailsViewModel(VaultAccount account, IArtworkService artworkService, AppSettings settings)
    {
        _account = account;
        _settings = settings;
        ArtworkService = artworkService;
        LootCategories = ["All", "Currencies", "Materials", "Champion shards", "Skin shards", "Other"];
        ChampionCollections = BuildChampionCollections();
        ChampionSortOptions = ["Name", "Mastery level", "Mastery points"];
        ChampionSortDirections = ["Ascending", "Descending"];
        BuildRankRows();
        ApplyFilters();
    }

    public Guid Id => _account.Id;
    public VaultAccount Account => _account;
    public IArtworkService ArtworkService { get; }
    public bool AllowCommunityDragon => _settings.DownloadCommunityDragonArtwork;
    public bool ShowExternalProfileLinks => _settings.ShowExternalProfileLinks && _settings.HasEnabledExternalProfileProvider;
    public bool CanOpenExternalProfileLinks => ShowExternalProfileLinks
        && ExternalProfileLinkBuilder.CanBuild(_account.RiotGameName, _account.RiotTagLine, _account.Region);
    public string ExternalProfileLinksToolTip => CanOpenExternalProfileLinks
        ? "Open this account on a third-party League statistics site"
        : "Sync this account to open external profiles";
    public bool ShowOpGgProfileLink => _settings.ShowOpGgProfileLink;
    public bool ShowDeepLolProfileLink => _settings.ShowDeepLolProfileLink;
    public bool ShowDpmLolProfileLink => _settings.ShowDpmLolProfileLink;
    public bool ShowLeagueOfGraphsProfileLink => _settings.ShowLeagueOfGraphsProfileLink;
    public string RiotId => string.IsNullOrWhiteSpace(_account.RiotGameName) ? _account.DisplayName : $"{_account.RiotGameName}#{_account.RiotTagLine}";
    public string ProfileLine => $"{_account.Region}  ·  Level {_account.SummonerLevel?.ToString(CultureInfo.CurrentCulture) ?? "not synced"}";
    public string RoleLine => _account.Roles == AccountRole.None ? "No role tags" : _account.Roles.ToString().Replace(",", " ·", StringComparison.Ordinal);
    public string LastPlayed => _account.MatchHistoryState switch { MatchHistoryState.NeverPlayed => "Never played", MatchHistoryState.Stale => $"Last played {_account.LastMatchPlayedAtUtc?.ToLocalTime():g} · data may be stale", MatchHistoryState.Known => $"Last played {_account.LastMatchPlayedAtUtc?.ToLocalTime():g}", _ => "Match history not synced" };
    public string RiotPoints => _account.RiotPoints?.ToString("N0", CultureInfo.CurrentCulture) ?? "Not synced";
    public string BlueEssence => _account.BlueEssence?.ToString("N0", CultureInfo.CurrentCulture) ?? "Not synced";
    public string OrangeEssence => CurrencyTotal("CURRENCY_cosmetic");
    public string MythicEssence => CurrencyTotal("CURRENCY_mythic", "Mythic Essence");
    public string Notes => string.IsNullOrWhiteSpace(_account.Notes) ? "No notes for this account." : _account.Notes;
    public string OwnershipSummary => $"{CurrentChampionCount} current champions · {ClassicChampionCount} League Classic champions · {_account.Skins.Count} skins · {_account.LootItems.Count} crafting entries";
    public string SyncSummary => _account.LastSyncedAtUtc is { } value ? $"Last synchronized {value.ToLocalTime():g}" : "League data has not been synchronized.";
    public IReadOnlyList<string> LootCategories { get; }
    public IReadOnlyList<string> ChampionCollections { get; }
    public IReadOnlyList<string> ChampionSortOptions { get; }
    public IReadOnlyList<string> ChampionSortDirections { get; }
    public int CurrentChampionCount => _account.Champions.Count(champion => champion.Variant == ChampionVariant.Current);
    public int ClassicChampionCount => _account.Champions.Count(champion => champion.Variant == ChampionVariant.LeagueClassic);
    public bool HasOtherChampions => _account.Champions.Any(champion => champion.Variant == ChampionVariant.Unknown);
    public bool IsChampionGalleryVisible => SelectedChampion is null;
    public bool IsChampionProgressionVisible => SelectedChampion is not null;
    public bool IsSynchronizationInProgress => !string.IsNullOrWhiteSpace(SynchronizationStatus);
    public ObservableCollection<ChampionGalleryItem> Champions { get; } = [];
    public ObservableCollection<SkinGalleryItem> Skins { get; } = [];
    public ObservableCollection<CraftingGalleryItem> Loot { get; } = [];
    public ObservableCollection<GalleryRow<ChampionGalleryItem>> ChampionRows { get; } = [];
    public ObservableCollection<GalleryRow<SkinGalleryItem>> SkinRows { get; } = [];
    public ObservableCollection<GalleryRow<CraftingGalleryItem>> LootRows { get; } = [];
    public ObservableCollection<GalleryRow<RankCardItem>> RankRows { get; } = [];
    public IReadOnlyList<RankCardItem> Ranks => [.. _account.Ranks.OrderBy(RankOrder).ThenBy(x => x.QueueType).Select(x => new RankCardItem(x))];
    public string ChampionCount => $"{Champions.Count} shown";
    public string SkinCount => $"{Skins.Count} shown";
    public string LootCount => $"{Loot.Count} shown";

    partial void OnChampionQueryChanged(string value) => Debounce();
    partial void OnChampionCollectionChanged(string value) => ApplyChampionFilter();
    partial void OnChampionSortChanged(string value) => ApplyChampionFilter();
    partial void OnChampionSortDirectionChanged(string value) => ApplyChampionFilter();
    partial void OnGalleryColumnCountChanged(int value)
    {
        BuildChampionRows();
        BuildSkinRows();
        BuildRankRows();
    }
    partial void OnCraftingColumnCountChanged(int value) => BuildLootRows();
    partial void OnSelectedChampionChanged(ChampionGalleryItem? value)
    {
        OnPropertyChanged(nameof(IsChampionGalleryVisible));
        OnPropertyChanged(nameof(IsChampionProgressionVisible));
    }
    partial void OnSkinQueryChanged(string value) => Debounce();
    partial void OnLootQueryChanged(string value) => Debounce();
    partial void OnLootCategoryChanged(string value) => ApplyFilters();
    partial void OnSynchronizationStatusChanged(string value) => OnPropertyChanged(nameof(IsSynchronizationInProgress));

    private async void Debounce()
    {
        _debounce?.Cancel(); _debounce?.Dispose(); _debounce = new();
        try { await Task.Delay(150, _debounce.Token); ApplyFilters(); } catch (OperationCanceledException) { }
    }

    private void ApplyFilters()
    {
        ApplyChampionFilter();

        Skins.Clear();
        var championNames = _account.Champions.ToDictionary(x => x.ChampionId, x => x.Name);
        foreach (OwnedSkin? skin in OwnedSkinRules.Normalize(_account.Skins).Where(x => Contains(x.Name, SkinQuery) || championNames.TryGetValue(x.ChampionId, out string? name) && Contains(name, SkinQuery)).OrderBy(x => x.Name))
        {
            Skins.Add(new(skin, championNames.GetValueOrDefault(skin.ChampionId) ?? "Unknown champion"));
        }

        Loot.Clear();
        foreach (CraftingGalleryItem? item in _account.LootItems.Select(CreateCraftingItem).Where(x => (LootCategory == "All" || x.Category.Equals(LootCategory, StringComparison.OrdinalIgnoreCase)) && (Contains(x.Name, LootQuery) || Contains(x.Item.LocalizedDescription, LootQuery))).OrderBy(x => x.Name))
        {
            Loot.Add(item);
        }

        BuildSkinRows();
        BuildLootRows();
        OnPropertyChanged(nameof(SkinCount)); OnPropertyChanged(nameof(LootCount));
    }

    public void SelectChampion(ChampionGalleryItem champion) => SelectedChampion = champion;
    public void ShowChampionGallery() => SelectedChampion = null;

    public void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
        OnPropertyChanged(nameof(AllowCommunityDragon));
        OnPropertyChanged(nameof(ShowExternalProfileLinks));
        OnPropertyChanged(nameof(CanOpenExternalProfileLinks));
        OnPropertyChanged(nameof(ExternalProfileLinksToolTip));
        OnPropertyChanged(nameof(ShowOpGgProfileLink));
        OnPropertyChanged(nameof(ShowDeepLolProfileLink));
        OnPropertyChanged(nameof(ShowDpmLolProfileLink));
        OnPropertyChanged(nameof(ShowLeagueOfGraphsProfileLink));
    }

    public void UpdateGalleryViewport(double availableWidth)
    {
        GalleryColumnCount = availableWidth >= 1250 ? 4 : availableWidth >= 900 ? 3 : 2;
        CraftingColumnCount = availableWidth >= 900 ? 3 : 2;
    }

    private void ApplyChampionFilter()
    {
        ChampionVariant variant = ChampionCollection switch
        {
            "League Classic" => ChampionVariant.LeagueClassic,
            "Other" => ChampionVariant.Unknown,
            _ => ChampionVariant.Current
        };
        Dictionary<int, ChampionMastery> masteries = _account.ChampionMasteries.ToDictionary(mastery => mastery.ChampionId);
        Dictionary<int, ChampionEternalSummary> summaries = _account.EternalSummaries.ToDictionary(summary => summary.ChampionId);
        SnapshotState masteryState = _account.SyncCategories.FirstOrDefault(status => status.Category == SnapshotCategory.Mastery)?.State ?? SnapshotState.Unknown;
        SnapshotState eternalState = _account.SyncCategories.FirstOrDefault(status => status.Category == SnapshotCategory.Eternals)?.State ?? SnapshotState.Unknown;
        IEnumerable<ChampionGalleryItem> items = _account.Champions
            .Where(champion => champion.Variant == variant && Contains(champion.Name, ChampionQuery))
            .Select(champion => new ChampionGalleryItem(champion, masteries.GetValueOrDefault(champion.ChampionId), summaries.GetValueOrDefault(champion.ChampionId),
                [.. _account.EternalSets.Where(set => set.ChampionId == champion.ChampionId).OrderBy(set => set.Name).Select(set => new EternalSetGalleryItem(set,
                    [.. _account.Eternals.Where(eternal => eternal.ChampionId == champion.ChampionId && eternal.SetId == set.SetId).OrderBy(eternal => eternal.Name).Select(eternal => new EternalGalleryItem(eternal))]))],
                masteryState,
                eternalState));
        bool descending = ChampionSortDirection == "Descending";
        items = ChampionSort switch
        {
            "Mastery level" => descending
                ? items.OrderBy(item => item.Mastery is null).ThenByDescending(item => item.Mastery?.Level).ThenByDescending(item => item.Mastery?.Points).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(item => item.Champion.ChampionId)
                : items.OrderBy(item => item.Mastery is null).ThenBy(item => item.Mastery?.Level).ThenBy(item => item.Mastery?.Points).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(item => item.Champion.ChampionId),
            "Mastery points" => descending
                ? items.OrderBy(item => item.Mastery is null).ThenByDescending(item => item.Mastery?.Points).ThenByDescending(item => item.Mastery?.Level).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(item => item.Champion.ChampionId)
                : items.OrderBy(item => item.Mastery is null).ThenBy(item => item.Mastery?.Points).ThenBy(item => item.Mastery?.Level).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(item => item.Champion.ChampionId),
            _ => descending
                ? items.OrderByDescending(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(item => item.Champion.ChampionId)
                : items.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(item => item.Champion.ChampionId)
        };

        Champions.Clear();
        foreach (ChampionGalleryItem item in items)
        {
            Champions.Add(item);
        }

        BuildChampionRows();
        OnPropertyChanged(nameof(ChampionCount));
    }

    private void BuildChampionRows() => BuildRows(Champions, ChampionRows, GalleryColumnCount);

    private void BuildSkinRows() => BuildRows(Skins, SkinRows, GalleryColumnCount);

    private void BuildRankRows() => BuildRows(Ranks, RankRows, GalleryColumnCount);

    private void BuildLootRows() => BuildRows(Loot, LootRows, CraftingColumnCount);

    private static void BuildRows<T>(IReadOnlyList<T> source, ObservableCollection<GalleryRow<T>> target, int columnCount) where T : class
    {
        target.Clear();
        for (int index = 0; index < source.Count; index += columnCount)
        {
            target.Add(new([.. source.Skip(index).Take(columnCount)]));
        }
    }

    private List<string> BuildChampionCollections()
    {
        var collections = new List<string> { "Current champions", "League Classic" };
        if (HasOtherChampions)
        {
            collections.Add("Other");
        }

        return collections;
    }

    private static bool Contains(string? value, string query) => string.IsNullOrWhiteSpace(query) || value?.Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase) == true;
    private static int RankOrder(RankSnapshot rank) => rank.QueueType switch { "RANKED_SOLO_5x5" => 0, "RANKED_FLEX_SR" => 1, _ => 2 };
    private CraftingGalleryItem CreateCraftingItem(CraftingLootItem item)
    {
        string category = NormalizeLootCategory(item);
        string? name = CurrencyName(item) ?? item.LocalizedName?.Trim();
        if (string.IsNullOrWhiteSpace(name) && int.TryParse(item.ReferenceId, out int referenceId))
        {
            if (category == "Skin shards" && _account.Skins.FirstOrDefault(x => x.SkinId == referenceId) is { } skin)
            {
                name = $"{skin.Name} shard";
            }
            else if (category == "Champion shards" && _account.Champions.FirstOrDefault(x => x.ChampionId == referenceId) is { } champion)
            {
                name = $"{champion.Name} shard";
            }
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            name = !string.IsNullOrWhiteSpace(item.LootName) ? Friendly(item.LootName) : category.TrimEnd('s');
        }

        return new(item, name, category);
    }
    private static string? CurrencyName(CraftingLootItem item)
    {
        string value = $"{item.LootId} {item.LootName} {item.LocalizedName}";
        if (value.Contains("CURRENCY_champion", StringComparison.OrdinalIgnoreCase))
        {
            return "Blue Essence";
        }

        if (value.Contains("CURRENCY_cosmetic", StringComparison.OrdinalIgnoreCase))
        {
            return "Orange Essence";
        }

        return null;
    }
    private string CurrencyTotal(params string[] markers)
    {
        long total = _account.LootItems.Where(item => markers.Any(marker => $"{item.LootId} {item.LootName} {item.LocalizedName}".Contains(marker, StringComparison.OrdinalIgnoreCase))).Sum(item => (long)item.Count);
        return total.ToString("N0", CultureInfo.CurrentCulture);
    }
    internal static string NormalizeLootCategory(CraftingLootItem item)
    {
        string value = $"{item.DisplayCategory} {item.Type} {item.LootName}";
        if (value.Contains("CURRENCY", StringComparison.OrdinalIgnoreCase))
        {
            return "Currencies";
        }

        if (value.Contains("SKIN", StringComparison.OrdinalIgnoreCase))
        {
            return "Skin shards";
        }

        if (value.Contains("CHAMPION", StringComparison.OrdinalIgnoreCase))
        {
            return "Champion shards";
        }

        if (value.Contains("MATERIAL", StringComparison.OrdinalIgnoreCase) || value.Contains("CHEST", StringComparison.OrdinalIgnoreCase) || value.Contains("KEY", StringComparison.OrdinalIgnoreCase) || value.Contains("TOKEN", StringComparison.OrdinalIgnoreCase))
        {
            return "Materials";
        }

        return "Other";
    }
    private static string Friendly(string value) => string.Join(' ', value.Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));
    public void Dispose() { _debounce?.Cancel(); _debounce?.Dispose(); Champions.Clear(); Skins.Clear(); Loot.Clear(); ChampionRows.Clear(); SkinRows.Clear(); LootRows.Clear(); RankRows.Clear(); }
}

public sealed record GalleryRow<T>(IReadOnlyList<T> Items) where T : class;

public sealed record ChampionGalleryItem(
    OwnedChampion Champion,
    ChampionMastery? Mastery,
    ChampionEternalSummary? EternalSummary,
    IReadOnlyList<EternalSetGalleryItem> EternalSets,
    SnapshotState MasteryState,
    SnapshotState EternalState)
{
    public string Name => Champion.Name;
    public string? Artwork => Champion.BaseSplashAssetPath ?? Champion.SquarePortraitAssetPath;
    public bool IsClassic => Champion.Variant == ChampionVariant.LeagueClassic;
    public string MasteryLevel => Mastery is null ? "Not synced" : $"Mastery {Mastery.Level}";
    public string MasteryPoints => Mastery is null ? "Mastery points not synced" : $"{Mastery.Points:N0} mastery points";
    public string EternalProgress => EternalSummary is null ? "Eternals not synced" : $"{EternalSummary.MilestonesPassed:N0} milestones · {EternalSummary.StonesIlluminated:N0} illuminated";
    public string ProgressionStatus => MasteryState == SnapshotState.Stale || EternalState == SnapshotState.Stale ? "Progression data may be stale" : string.Empty;
    public string EternalEmptyMessage => EternalSets.Count == 0 ? EternalSummary is null ? "Eternals have not been synchronized." : "No owned Eternals for this champion." : string.Empty;
    public string HighestGrade => string.IsNullOrWhiteSpace(Mastery?.HighestGrade) ? "No highest grade" : $"Highest grade {Mastery.HighestGrade}";
    public string LastMasteryPlay => Mastery?.LastPlayAtUtc is { } played ? $"Last mastery play {played.ToLocalTime():g}" : "Last mastery play not available";
    public string MasteryProgress => Mastery is null ? "Mastery is not synchronized." : Mastery.PointsUntilNextLevel > 0
        ? $"{Mastery.PointsSinceLastLevel:N0} earned · {Mastery.PointsUntilNextLevel:N0} until next level"
        : $"{Mastery.TokensEarned:N0} marks earned · {Mastery.MarksRequiredForNextLevel:N0} required for next level";
}

public sealed record EternalSetGalleryItem(ChampionEternalSet Set, IReadOnlyList<EternalGalleryItem> Eternals)
{
    public string Name => Set.Name;
    public string Summary => $"{Set.MilestonesPassed:N0} milestones · {Set.StonesIlluminated:N0} illuminated";
}

public sealed record EternalGalleryItem(ChampionEternal Eternal)
{
    public string Name => Eternal.Name;
    public string? Description => string.Join(" · ", new[] { Eternal.Description, Progress }.Where(value => !string.IsNullOrWhiteSpace(value)));
    public string Value => string.IsNullOrWhiteSpace(Eternal.FormattedValue) ? Eternal.Value.ToString("N0", CultureInfo.CurrentCulture) : Eternal.FormattedValue;
    public string FormattedValue { get; set; } = string.IsNullOrWhiteSpace(Eternal.FormattedValue)
        ? Eternal.Value.ToString("N0", CultureInfo.CurrentCulture)
        : Eternal.FormattedValue;
    public int MilestoneLevel { get; set; } = Eternal.MilestoneLevel;
    public string Progress => string.Join(" · ", new[]
    {
        Eternal.NextMilestone is { } next ? $"Next milestone {next:N0}" : null,
        Eternal.PersonalBest is { } best ? $"Personal best {Eternal.FormattedPersonalBest ?? best.ToString("N0", CultureInfo.CurrentCulture)}" : null
    }.Where(value => value is not null));
    public bool IsFeatured => Eternal.IsFeatured;
    public bool IsComplete => Eternal.IsComplete;
    public bool IsRetired => Eternal.IsRetired;
}
public sealed record SkinGalleryItem(OwnedSkin Skin, string ChampionName) { public string Name => Skin.Name; public string Subtitle => ChampionName; public string? Artwork => Skin.SplashAssetPath ?? Skin.TileAssetPath; }
public sealed record CraftingGalleryItem(CraftingLootItem Item, string Name, string Category)
{
    public string Subtitle => $"{Category} · {Item.Rarity ?? "Standard"}";
    public string Quantity => $"×{Item.Count:N0}";
    public string? Artwork => Item.TileAssetPath ?? Item.AssetPath ?? Item.SplashAssetPath;
    public string Values => string.Join(" · ", new[] { Item.DisenchantValue is { } d ? $"Disenchant {d:N0}" : null, Item.UpgradeEssenceValue is { } u ? $"Upgrade {u:N0}" : null, Item.ExpiresAtUtc is { } e ? $"Expires {e.ToLocalTime():g}" : null }.Where(x => x is not null));
}
public sealed class RankCardItem(RankSnapshot rank)
{
    private readonly RankSnapshot _rank = rank;

    public RankSnapshot Rank => _rank;
    public string Queue => _rank.QueueType switch { "RANKED_SOLO_5x5" => "Solo / Duo", "RANKED_FLEX_SR" => "Ranked Flex", "JADE_RANKED_SOLO_5x5" or "JADE_SOLO_5x5" => "League Classic Solo / Duo", _ => _rank.QueueType.Replace('_', ' ') };
    private bool HasRatedRank => !string.IsNullOrWhiteSpace(_rank.RatedTier) && !_rank.RatedTier.Equals("NONE", StringComparison.OrdinalIgnoreCase) && _rank.RatedRating is > 0;
    public string RankText => HasRatedRank ? $"{Title(_rank.RatedTier!)} · {_rank.RatedRating:N0} rating" : IsUnranked ? "Unranked" : $"{Title(_rank.Tier)} {_rank.Division} · {_rank.LeaguePoints} LP";
    public string Record => $"{_rank.Wins} wins · {_rank.Losses} losses · {_rank.Wins + _rank.Losses} games";
    public string WinRate => _rank.Wins + _rank.Losses == 0 ? "No completed games" : $"{(double)_rank.Wins / (_rank.Wins + _rank.Losses):P0} win rate";
    public string Placement => _rank.IsProvisional ? $"{_rank.ProvisionalGamesRemaining ?? 0} placement games remaining" : string.Empty;
    public Uri? Icon => HasRatedRank || IsUnranked ? null : RankIconCatalog.GetUri(_rank.Tier);
    private bool IsUnranked => string.IsNullOrWhiteSpace(_rank.Tier) || _rank.Tier.Equals("NONE", StringComparison.OrdinalIgnoreCase) || _rank.Tier.Equals("UNRANKED", StringComparison.OrdinalIgnoreCase);
    private static string Title(string value) => string.IsNullOrWhiteSpace(value) ? "Unranked" : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
}
