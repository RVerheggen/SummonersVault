using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SummonersVault.Core.Abstractions;
using SummonersVault.Core.Models;
using SummonersVault.Infrastructure.Settings;

namespace SummonersVault.App.ViewModels;

public sealed partial class AccountDetailsViewModel : ObservableObject, IDisposable
{
    private readonly VaultAccount _account;
    private CancellationTokenSource? _debounce;
    [ObservableProperty] private string championQuery = string.Empty;
    [ObservableProperty] private string skinQuery = string.Empty;
    [ObservableProperty] private string lootQuery = string.Empty;
    [ObservableProperty] private string lootCategory = "All";

    public AccountDetailsViewModel(VaultAccount account, IArtworkService artworkService, AppSettings settings)
    {
        _account = account;
        ArtworkService = artworkService;
        AllowCommunityDragon = settings.DownloadCommunityDragonArtwork;
        LootCategories = ["All", "Currencies", "Materials", "Champion shards", "Skin shards", "Other"];
        Pair(Ranks, RankRows);
        ApplyFilters();
    }

    public Guid Id => _account.Id;
    public VaultAccount Account => _account;
    public IArtworkService ArtworkService { get; }
    public bool AllowCommunityDragon { get; }
    public string RiotId => string.IsNullOrWhiteSpace(_account.RiotGameName) ? _account.DisplayName : $"{_account.RiotGameName}#{_account.RiotTagLine}";
    public string ProfileLine => $"{_account.Region}  ·  Level {_account.SummonerLevel?.ToString() ?? "not synced"}";
    public string RoleLine => _account.Roles == AccountRole.None ? "No role tags" : _account.Roles.ToString().Replace(",", " ·", StringComparison.Ordinal);
    public string LastPlayed => _account.MatchHistoryState switch { MatchHistoryState.NeverPlayed => "Never played", MatchHistoryState.Stale => $"Last played {_account.LastMatchPlayedAtUtc?.ToLocalTime():g} · data may be stale", MatchHistoryState.Known => $"Last played {_account.LastMatchPlayedAtUtc?.ToLocalTime():g}", _ => "Match history not synced" };
    public string RiotPoints => _account.RiotPoints?.ToString("N0") ?? "Not synced";
    public string BlueEssence => _account.BlueEssence?.ToString("N0") ?? "Not synced";
    public string OrangeEssence => CurrencyTotal("CURRENCY_cosmetic");
    public string MythicEssence => CurrencyTotal("CURRENCY_mythic", "Mythic Essence");
    public string Notes => string.IsNullOrWhiteSpace(_account.Notes) ? "No notes for this account." : _account.Notes;
    public string OwnershipSummary => $"{_account.Champions.Count} champions · {_account.Skins.Count} skins · {_account.LootItems.Count} crafting entries";
    public string SyncSummary => _account.LastSyncedAtUtc is { } value ? $"Last synchronized {value.ToLocalTime():g}" : "League data has not been synchronized.";
    public IReadOnlyList<string> LootCategories { get; }
    public ObservableCollection<ChampionGalleryItem> Champions { get; } = [];
    public ObservableCollection<SkinGalleryItem> Skins { get; } = [];
    public ObservableCollection<CraftingGalleryItem> Loot { get; } = [];
    public ObservableCollection<GalleryPair<ChampionGalleryItem>> ChampionRows { get; } = [];
    public ObservableCollection<GalleryPair<SkinGalleryItem>> SkinRows { get; } = [];
    public ObservableCollection<GalleryPair<CraftingGalleryItem>> LootRows { get; } = [];
    public ObservableCollection<GalleryPair<RankCardItem>> RankRows { get; } = [];
    public IReadOnlyList<RankCardItem> Ranks => _account.Ranks.OrderBy(RankOrder).ThenBy(x => x.QueueType).Select(x => new RankCardItem(x)).ToArray();
    public string ChampionCount => $"{Champions.Count} shown";
    public string SkinCount => $"{Skins.Count} shown";
    public string LootCount => $"{Loot.Count} shown";

    partial void OnChampionQueryChanged(string value) => Debounce();
    partial void OnSkinQueryChanged(string value) => Debounce();
    partial void OnLootQueryChanged(string value) => Debounce();
    partial void OnLootCategoryChanged(string value) => ApplyFilters();

    private async void Debounce()
    {
        _debounce?.Cancel(); _debounce?.Dispose(); _debounce = new();
        try { await Task.Delay(150, _debounce.Token); ApplyFilters(); } catch (OperationCanceledException) { }
    }

    private void ApplyFilters()
    {
        Champions.Clear();
        foreach (var champion in _account.Champions.Where(x => Contains(x.Name, ChampionQuery)).OrderBy(x => x.Name)) Champions.Add(new(champion));
        Skins.Clear();
        var championNames = _account.Champions.ToDictionary(x => x.ChampionId, x => x.Name);
        foreach (var skin in OwnedSkinRules.Normalize(_account.Skins).Where(x => Contains(x.Name, SkinQuery) || championNames.TryGetValue(x.ChampionId, out var name) && Contains(name, SkinQuery)).OrderBy(x => x.Name))
            Skins.Add(new(skin, championNames.GetValueOrDefault(skin.ChampionId) ?? "Unknown champion"));
        Loot.Clear();
        foreach (var item in _account.LootItems.Select(CreateCraftingItem).Where(x => (LootCategory == "All" || x.Category.Equals(LootCategory, StringComparison.OrdinalIgnoreCase)) && (Contains(x.Name, LootQuery) || Contains(x.Item.LocalizedDescription, LootQuery))).OrderBy(x => x.Name)) Loot.Add(item);
        Pair(Champions, ChampionRows); Pair(Skins, SkinRows); Pair(Loot, LootRows);
        OnPropertyChanged(nameof(ChampionCount)); OnPropertyChanged(nameof(SkinCount)); OnPropertyChanged(nameof(LootCount));
    }

    private static bool Contains(string? value, string query) => string.IsNullOrWhiteSpace(query) || value?.Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase) == true;
    private static void Pair<T>(IReadOnlyList<T> source, ObservableCollection<GalleryPair<T>> target) where T : class
    {
        target.Clear();
        for (var index = 0; index < source.Count; index += 2) target.Add(new(source[index], index + 1 < source.Count ? source[index + 1] : null));
    }
    private static int RankOrder(RankSnapshot rank) => rank.QueueType switch { "RANKED_SOLO_5x5" => 0, "RANKED_FLEX_SR" => 1, _ => 2 };
    private CraftingGalleryItem CreateCraftingItem(CraftingLootItem item)
    {
        var category = NormalizeLootCategory(item);
        var name = CurrencyName(item) ?? item.LocalizedName?.Trim();
        if (string.IsNullOrWhiteSpace(name) && int.TryParse(item.ReferenceId, out var referenceId))
        {
            if (category == "Skin shards" && _account.Skins.FirstOrDefault(x => x.SkinId == referenceId) is { } skin) name = $"{skin.Name} shard";
            else if (category == "Champion shards" && _account.Champions.FirstOrDefault(x => x.ChampionId == referenceId) is { } champion) name = $"{champion.Name} shard";
        }
        if (string.IsNullOrWhiteSpace(name)) name = !string.IsNullOrWhiteSpace(item.LootName) ? Friendly(item.LootName) : category.TrimEnd('s');
        return new(item, name, category);
    }
    private static string? CurrencyName(CraftingLootItem item)
    {
        var value = $"{item.LootId} {item.LootName} {item.LocalizedName}";
        if (value.Contains("CURRENCY_champion", StringComparison.OrdinalIgnoreCase)) return "Blue Essence";
        if (value.Contains("CURRENCY_cosmetic", StringComparison.OrdinalIgnoreCase)) return "Orange Essence";
        return null;
    }
    private string CurrencyTotal(params string[] markers)
    {
        var total = _account.LootItems.Where(item => markers.Any(marker => $"{item.LootId} {item.LootName} {item.LocalizedName}".Contains(marker, StringComparison.OrdinalIgnoreCase))).Sum(item => (long)item.Count);
        return total.ToString("N0");
    }
    internal static string NormalizeLootCategory(CraftingLootItem item)
    {
        var value = $"{item.DisplayCategory} {item.Type} {item.LootName}";
        if (value.Contains("CURRENCY", StringComparison.OrdinalIgnoreCase)) return "Currencies";
        if (value.Contains("SKIN", StringComparison.OrdinalIgnoreCase)) return "Skin shards";
        if (value.Contains("CHAMPION", StringComparison.OrdinalIgnoreCase)) return "Champion shards";
        if (value.Contains("MATERIAL", StringComparison.OrdinalIgnoreCase) || value.Contains("CHEST", StringComparison.OrdinalIgnoreCase) || value.Contains("KEY", StringComparison.OrdinalIgnoreCase) || value.Contains("TOKEN", StringComparison.OrdinalIgnoreCase)) return "Materials";
        return "Other";
    }
    private static string Friendly(string value) => string.Join(' ', value.Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));
    public void Dispose() { _debounce?.Cancel(); _debounce?.Dispose(); Champions.Clear(); Skins.Clear(); Loot.Clear(); ChampionRows.Clear(); SkinRows.Clear(); LootRows.Clear(); RankRows.Clear(); }
}

public sealed record GalleryPair<T>(T First, T? Second) where T : class;

public sealed record ChampionGalleryItem(OwnedChampion Champion) { public string Name => Champion.Name; public string? Artwork => Champion.BaseSplashAssetPath ?? Champion.SquarePortraitAssetPath; }
public sealed record SkinGalleryItem(OwnedSkin Skin, string ChampionName) { public string Name => Skin.Name; public string Subtitle => ChampionName; public string? Artwork => Skin.SplashAssetPath ?? Skin.TileAssetPath; }
public sealed record CraftingGalleryItem(CraftingLootItem Item, string Name, string Category)
{
    public string Subtitle => $"{Category} · {Item.Rarity ?? "Standard"}";
    public string Quantity => $"×{Item.Count:N0}";
    public string? Artwork => Item.TileAssetPath ?? Item.AssetPath ?? Item.SplashAssetPath;
    public string Values => string.Join(" · ", new[] { Item.DisenchantValue is { } d ? $"Disenchant {d:N0}" : null, Item.UpgradeEssenceValue is { } u ? $"Upgrade {u:N0}" : null, Item.ExpiresAtUtc is { } e ? $"Expires {e.ToLocalTime():g}" : null }.Where(x => x is not null));
}
public sealed class RankCardItem
{
    private readonly RankSnapshot rank;
    public RankCardItem(RankSnapshot rank) { this.rank = rank; }
    public RankSnapshot Rank => rank;
    public string Queue => rank.QueueType switch { "RANKED_SOLO_5x5" => "Solo / Duo", "RANKED_FLEX_SR" => "Ranked Flex", "JADE_RANKED_SOLO_5x5" or "JADE_SOLO_5x5" => "League Classic Solo / Duo", _ => rank.QueueType.Replace('_', ' ') };
    private bool HasRatedRank => !string.IsNullOrWhiteSpace(rank.RatedTier) && !rank.RatedTier.Equals("NONE", StringComparison.OrdinalIgnoreCase) && rank.RatedRating is > 0;
    public string RankText => HasRatedRank ? $"{Title(rank.RatedTier!)} · {rank.RatedRating:N0} rating" : IsUnranked ? "Unranked" : $"{Title(rank.Tier)} {rank.Division} · {rank.LeaguePoints} LP";
    public string Record => $"{rank.Wins} wins · {rank.Losses} losses · {rank.Wins + rank.Losses} games";
    public string WinRate => rank.Wins + rank.Losses == 0 ? "No completed games" : $"{(double)rank.Wins / (rank.Wins + rank.Losses):P0} win rate";
    public string Placement => rank.IsProvisional ? $"{rank.ProvisionalGamesRemaining ?? 0} placement games remaining" : string.Empty;
    public Uri? Icon => HasRatedRank || IsUnranked ? null : RankIconCatalog.GetUri(rank.Tier);
    private bool IsUnranked => string.IsNullOrWhiteSpace(rank.Tier) || rank.Tier.Equals("NONE", StringComparison.OrdinalIgnoreCase) || rank.Tier.Equals("UNRANKED", StringComparison.OrdinalIgnoreCase);
    private static string Title(string value) => string.IsNullOrWhiteSpace(value) ? "Unranked" : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
}
