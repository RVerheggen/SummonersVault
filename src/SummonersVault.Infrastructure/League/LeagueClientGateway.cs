using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SummonersVault.Core.Abstractions;
using SummonersVault.Core.Models;

namespace SummonersVault.Infrastructure.League;

public sealed class LeagueClientGateway : ILeagueClientGateway
{
    private string? _configuredInstallDirectory;

    public void SetConfiguredInstallDirectory(string? directory) => _configuredInstallDirectory = directory;

    public async Task<LeagueClientStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var lockfile = await FindLockfileAsync(cancellationToken).ConfigureAwait(false);
        if (lockfile is null) return new(false, false, "League Client is not running");
        try
        {
            using var client = CreateClient(lockfile);
            using var response = await client.GetAsync("lol-summoner/v1/current-summoner", cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? new(true, true, "League Client connected")
                : new(true, false, "League Client is waiting for sign-in");
        }
        catch (HttpRequestException) { return new(true, false, "League Client is starting"); }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(true, false, "League Client is starting"); }
    }

    public async Task<LeagueSnapshot> FetchCurrentSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var lockfile = await FindLockfileAsync(cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("League Client is not running.");
        using var client = CreateClient(lockfile);
        using var summoner = await GetJsonAsync(client, "lol-summoner/v1/current-summoner", cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Sign into League Client before synchronizing.");
        var root = summoner.RootElement;
        var puuid = GetString(root, "puuid") ?? throw new InvalidDataException("League Client did not return a PUUID.");
        var summonerId = GetInt64(root, "summonerId");
        var summonerLevel = GetInt32(root, "summonerLevel");
        var riotName = GetString(root, "gameName") ?? GetString(root, "displayName") ?? "Unknown";
        var tag = GetString(root, "tagLine") ?? string.Empty;
        var iconId = GetInt32(root, "profileIconId");

        var region = "UNKNOWN";
        using (var regionJson = await TryGetJsonAsync(client, "riotclient/region-locale", cancellationToken).ConfigureAwait(false))
            if (regionJson is not null)
            {
                var normalizedRegion = LeagueRegion.Normalize(GetString(regionJson.RootElement, "region"));
                if (!string.IsNullOrEmpty(normalizedRegion)) region = normalizedRegion;
            }

        var ranks = await FetchRanksAsync(client, cancellationToken).ConfigureAwait(false);
        var wallet = await FetchWalletAsync(client, cancellationToken).ConfigureAwait(false);
        var champions = summonerId.HasValue ? await FetchChampionsAsync(client, summonerId.Value, cancellationToken).ConfigureAwait(false) : null;
        var skins = summonerId.HasValue ? await FetchSkinsAsync(client, summonerId.Value, cancellationToken).ConfigureAwait(false) : null;
        var craftingLoot = await FetchCraftingLootAsync(client, champions, skins, cancellationToken).ConfigureAwait(false);
        var match = await FetchLatestMatchAsync(client, puuid, cancellationToken).ConfigureAwait(false);
        byte[]? icon = null;
        if (iconId.HasValue)
        {
            try
            {
                using var response = await client.GetAsync($"lol-game-data/assets/v1/profile-icons/{iconId.Value}.jpg", cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode) icon = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException) { }
        }

        return new LeagueSnapshot
        {
            Puuid = puuid, SummonerId = summonerId, RiotGameName = riotName, RiotTagLine = tag, Region = region,
            ProfileIconId = iconId, ProfileIconBytes = icon, SummonerLevel = summonerLevel, Wallet = wallet,
            Ranks = ranks, Champions = champions, Skins = skins, CraftingLoot = craftingLoot, Match = match
        };
    }

    public async Task<byte[]?> FetchAssetAsync(string assetPath, CancellationToken cancellationToken = default)
    {
        if (!IsSafeAssetPath(assetPath)) return null;
        var lockfile = await FindLockfileAsync(cancellationToken).ConfigureAwait(false);
        if (lockfile is null) return null;
        try
        {
            using var client = CreateClient(lockfile);
            using var response = await client.GetAsync(assetPath.TrimStart('/'), cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 8 * 1024 * 1024) return null;
            return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException) { return null; }
    }

    public Task<bool> LaunchAsync(string? configuredInstallDirectory, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _configuredInstallDirectory = configuredInstallDirectory ?? _configuredInstallDirectory;
        var executable = CandidateRiotClientExecutables().FirstOrDefault(File.Exists);
        if (executable is null) return Task.FromResult(false);
        try
        {
            Process.Start(LeagueInstallationLocator.CreateLaunchStartInfo(executable));
            return Task.FromResult(true);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return Task.FromResult(false);
        }
    }

    private async Task<LeagueLockfile?> FindLockfileAsync(CancellationToken cancellationToken)
    {
        foreach (var directory in CandidateLeagueDirectories())
        {
            var path = Path.Combine(directory, "lockfile");
            if (!File.Exists(path)) continue;
            try
            {
                var content = await ReadLockfileAsync(path, cancellationToken).ConfigureAwait(false);
                if (LeagueLockfile.TryParse(content, out var lockfile)) return lockfile;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return null;
    }

    internal static async Task<string> ReadLockfileAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 256,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    private IReadOnlyList<string> CandidateLeagueDirectories() => LeagueInstallationLocator.GetLeagueDirectories(
        _configuredInstallDirectory,
        LeagueInstallationLocator.DefaultRiotGamesDirectory,
        GetRunningExecutablePaths("LeagueClient", "LeagueClientUx"));

    private IReadOnlyList<string> CandidateRiotClientExecutables() => LeagueInstallationLocator.GetRiotClientExecutables(
        _configuredInstallDirectory,
        LeagueInstallationLocator.DefaultRiotGamesDirectory,
        GetRunningExecutablePaths("RiotClientServices"));

    private static IEnumerable<string> GetRunningExecutablePaths(params string[] processNames)
    {
        foreach (var processName in processNames)
        {
            Process[] processes = [];
            try { processes = Process.GetProcessesByName(processName); } catch (InvalidOperationException) { }
            foreach (var process in processes)
            {
                using (process)
                {
                    string? path = null;
                    try { path = process.MainModule?.FileName; }
                    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException) { }
                    if (!string.IsNullOrWhiteSpace(path)) yield return path;
                }
            }
        }
    }

    private static HttpClient CreateClient(LeagueLockfile lockfile)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (request, _, _, _) => IsLeagueLoopbackRequest(request.RequestUri)
        };
        var client = new HttpClient(handler) { BaseAddress = lockfile.BaseUri, Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"riot:{lockfile.Password}")));
        return client;
    }

    internal static bool IsLeagueLoopbackRequest(Uri? uri) =>
        uri is { IsLoopback: true, Host: "127.0.0.1", Scheme: "https" };

    internal static bool IsInventoryPayloadReady(JsonElement element) =>
        element.ValueKind == JsonValueKind.Array;

    internal static LeagueWalletSnapshot? ParseWallet(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object
            ? new LeagueWalletSnapshot(GetWalletValue(element, "rp"), GetWalletValue(element, "lol_blue_essence", "ip", "blueEssence", "be"))
            : null;

    private static async Task<LeagueWalletSnapshot?> FetchWalletAsync(HttpClient client, CancellationToken cancellationToken)
    {
        LeagueWalletSnapshot? partial = null;
        foreach (var path in new[] { "lol-login/v1/wallet", "lol-store/v1/wallet" })
        {
            using var json = await TryGetJsonAsync(client, path, cancellationToken).ConfigureAwait(false);
            if (json is null) continue;
            var wallet = ParseWallet(json.RootElement);
            if (wallet is null) continue;
            partial = MergeWallet(partial, wallet);
            if (partial is { RiotPoints: not null, BlueEssence: not null }) return partial;
        }

        var riotPoints = partial?.RiotPoints
            ?? await FetchWalletCurrencyAsync(client, "RP", cancellationToken).ConfigureAwait(false);
        var blueEssence = partial?.BlueEssence
            ?? await FetchWalletCurrencyAsync(client, "lol_blue_essence", cancellationToken).ConfigureAwait(false)
            ?? await FetchWalletCurrencyAsync(client, "IP", cancellationToken).ConfigureAwait(false);

        if (riotPoints.HasValue || blueEssence.HasValue)
            return new LeagueWalletSnapshot(riotPoints, blueEssence);

        return partial;
    }

    private static LeagueWalletSnapshot MergeWallet(LeagueWalletSnapshot? current, LeagueWalletSnapshot incoming) =>
        new(current?.RiotPoints ?? incoming.RiotPoints, current?.BlueEssence ?? incoming.BlueEssence);

    private static async Task<long?> FetchWalletCurrencyAsync(
        HttpClient client,
        string currencyType,
        CancellationToken cancellationToken)
    {
        using var json = await TryGetJsonAsync(
            client,
            $"lol-inventory/v1/wallet/{currencyType}",
            cancellationToken).ConfigureAwait(false);

        return json is null ? null : ParseWalletCurrency(json.RootElement, currencyType);
    }

    internal static long? ParseWalletCurrency(JsonElement element, string currencyType) =>
        TryGetNumericValue(element)
        ?? (element.ValueKind == JsonValueKind.Object
            ? GetWalletValue(element, currencyType, "amount", "balance", "quantity", "value")
            : null);

    private static long? GetWalletValue(JsonElement element, params string[] names)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Any(name => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
            var value = TryGetNumericValue(property.Value);
            if (value.HasValue) return value;
        }
        return null;
    }

    private static long? TryGetNumericValue(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var numeric)) return numeric;
        if (element.ValueKind == JsonValueKind.String
            && long.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out numeric)) return numeric;
        return null;
    }

    private static async Task<IReadOnlyList<RankSnapshot>?> FetchRanksAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var json = await TryGetJsonAsync(client, "lol-ranked/v1/current-ranked-stats", cancellationToken).ConfigureAwait(false);
        if (json is null) return null;
        var element = json.RootElement;
        if (element.TryGetProperty("queues", out var queues)) element = queues;
        if (element.ValueKind != JsonValueKind.Array) return [];
        var result = new List<RankSnapshot>();
        foreach (var item in element.EnumerateArray())
        {
            var queue = GetString(item, "queueType");
            if (string.IsNullOrWhiteSpace(queue)) continue;
            if (queue.Equals("NONE", StringComparison.OrdinalIgnoreCase)) continue;
            result.Add(new(queue, GetString(item, "tier") ?? "UNRANKED", GetString(item, "division") ?? string.Empty,
                GetInt32(item, "leaguePoints") ?? 0, GetInt32(item, "wins") ?? 0, GetInt32(item, "losses") ?? 0,
                GetBoolean(item, "isProvisional") ?? false, GetInt32(item, "provisionalGamesRemaining"),
                GetString(item, "ratedTier"), GetInt32(item, "ratedRating")));
        }
        return result;
    }

    private static async Task<IReadOnlyList<OwnedChampion>?> FetchChampionsAsync(HttpClient client, long summonerId, CancellationToken cancellationToken)
    {
        using var json = await TryGetJsonAsync(client, $"lol-champions/v1/inventories/{summonerId}/champions-minimal", cancellationToken).ConfigureAwait(false);
        if (json is null || !IsInventoryPayloadReady(json.RootElement)) return null;
        return json.RootElement.EnumerateArray().Where(IsOwned).Select(x => new OwnedChampion(
            GetInt32(x, "id") ?? 0, GetString(x, "name") ?? "Unknown champion",
            GetString(x, "baseSplashPath"), GetString(x, "squarePortraitPath"))).Where(x => x.ChampionId > 0).ToArray();
    }

    private static async Task<IReadOnlyList<OwnedSkin>?> FetchSkinsAsync(HttpClient client, long summonerId, CancellationToken cancellationToken)
    {
        using var json = await TryGetJsonAsync(client, $"lol-champions/v1/inventories/{summonerId}/skins-minimal", cancellationToken).ConfigureAwait(false);
        if (json is null || !IsInventoryPayloadReady(json.RootElement)) return null;
        var skins = json.RootElement.EnumerateArray()
            .Where(IsOwned)
            .Where(x => GetBoolean(x, "isBase") != true)
            .Select(x => new OwnedSkin(GetInt32(x, "id") ?? 0, GetInt32(x, "championId") ?? 0, GetString(x, "name") ?? "Unknown skin",
                GetString(x, "splashPath"), GetString(x, "tilePath")));
        return OwnedSkinRules.Normalize(skins);
    }

    private static async Task<IReadOnlyList<CraftingLootItem>?> FetchCraftingLootAsync(HttpClient client, IReadOnlyList<OwnedChampion>? champions, IReadOnlyList<OwnedSkin>? skins, CancellationToken cancellationToken)
    {
        using var readyJson = await TryGetJsonAsync(client, "lol-loot/v1/ready", cancellationToken).ConfigureAwait(false);
        if (readyJson is null || !ParseLootReady(readyJson.RootElement)) return null;
        using var json = await TryGetJsonAsync(client, "lol-loot/v1/player-loot", cancellationToken).ConfigureAwait(false);
        if (json is null) return null;
        return EnrichCraftingNames(ParseCraftingLoot(json.RootElement), champions, skins);
    }

    internal static bool ParseLootReady(JsonElement element) => element.ValueKind == JsonValueKind.True
        || element.ValueKind == JsonValueKind.Object && GetBoolean(element, "ready") == true;

    internal static IReadOnlyList<CraftingLootItem> ParseCraftingLoot(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array) return [];
        var result = new List<CraftingLootItem>();
        foreach (var item in element.EnumerateArray())
        {
            var count = GetInt32(item, "count") ?? 0;
            var lootId = GetString(item, "lootId");
            if (count <= 0 || string.IsNullOrWhiteSpace(lootId)) continue;
            var lootName = FirstNonBlank(GetString(item, "lootName"), lootId);
            var type = GetString(item, "type") ?? "Other";
            var display = CategorizeLoot(type, lootName, GetString(item, "displayCategories"));
            var localizedName = CurrencyDisplayName(lootId, lootName)
                ?? FirstNonBlank(GetString(item, "localizedName"), GetString(item, "itemDesc"), lootName);
            result.Add(new(lootId, lootName, type, display,
                localizedName, GetString(item, "localizedDescription"), count,
                GetString(item, "rarity"), GetScalarString(item, "refId"), GetString(item, "asset"),
                GetString(item, "splashPath"), GetString(item, "tilePath"), ParseEpoch(GetInt64(item, "expiryTime")),
                GetInt32(item, "disenchantValue"), GetInt32(item, "upgradeEssenceValue")));
        }
        return result;
    }

    private static string CategorizeLoot(string type, string name, string? displayCategory)
    {
        var value = $"{displayCategory} {type} {name}";
        if (value.Contains("CURRENCY", StringComparison.OrdinalIgnoreCase)) return "Currencies";
        if (value.Contains("CHAMPION", StringComparison.OrdinalIgnoreCase)) return "Champion shards";
        if (value.Contains("SKIN", StringComparison.OrdinalIgnoreCase)) return "Skin shards";
        if (value.Contains("MATERIAL", StringComparison.OrdinalIgnoreCase) || value.Contains("CHEST", StringComparison.OrdinalIgnoreCase) || value.Contains("KEY", StringComparison.OrdinalIgnoreCase) || value.Contains("TOKEN", StringComparison.OrdinalIgnoreCase)) return "Materials";
        return "Other";
    }

    private static string? CurrencyDisplayName(string lootId, string lootName)
    {
        var value = $"{lootId} {lootName}";
        if (value.Contains("CURRENCY_champion", StringComparison.OrdinalIgnoreCase)) return "Blue Essence";
        if (value.Contains("CURRENCY_cosmetic", StringComparison.OrdinalIgnoreCase)) return "Orange Essence";
        return null;
    }

    private static IReadOnlyList<CraftingLootItem> EnrichCraftingNames(IReadOnlyList<CraftingLootItem> items, IReadOnlyList<OwnedChampion>? champions, IReadOnlyList<OwnedSkin>? skins)
    {
        var championNames = champions?.ToDictionary(x => x.ChampionId, x => x.Name) ?? [];
        var skinNames = skins?.ToDictionary(x => x.SkinId, x => x.Name) ?? [];
        return items.Select(item =>
        {
            if (!string.IsNullOrWhiteSpace(item.LocalizedName)
                && !item.LocalizedName.Equals(item.LootId, StringComparison.OrdinalIgnoreCase)
                && !item.LocalizedName.Equals(item.LootName, StringComparison.OrdinalIgnoreCase)) return item;
            if (!int.TryParse(item.ReferenceId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var referenceId)) return item;
            if (item.DisplayCategory == "Champion shards" && championNames.TryGetValue(referenceId, out var championName)) return item with { LocalizedName = $"{championName} shard" };
            if (item.DisplayCategory == "Skin shards" && skinNames.TryGetValue(referenceId, out var skinName)) return item with { LocalizedName = $"{skinName} shard" };
            return item;
        }).ToArray();
    }

    private static string FirstNonBlank(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "Unknown item";
    private static string? GetScalarString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ValueKind == JsonValueKind.Number ? value.GetRawText() : null;
    }

    private static DateTimeOffset? ParseEpoch(long? value)
    {
        if (!value.HasValue || value <= 0) return null;
        try { return value > 10_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(value.Value) : DateTimeOffset.FromUnixTimeSeconds(value.Value); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    internal static bool IsSafeAssetPath(string? path) => !string.IsNullOrWhiteSpace(path)
        && path.StartsWith('/') && !path.Contains("..", StringComparison.Ordinal) && !path.Contains('\\')
        && !Uri.TryCreate(path, UriKind.Absolute, out _);

    private static async Task<MatchSnapshotResult> FetchLatestMatchAsync(HttpClient client, string puuid, CancellationToken cancellationToken)
    {
        using var json = await TryGetJsonAsync(client, $"lol-match-history/v1/products/lol/{Uri.EscapeDataString(puuid)}/matches?begIndex=0&endIndex=1", cancellationToken).ConfigureAwait(false);
        if (json is null) return MatchSnapshotResult.Failed;
        return LeagueMatchHistoryParser.Parse(json.RootElement);
    }

    private static bool IsOwned(JsonElement item)
    {
        if (item.TryGetProperty("ownership", out var ownership) && ownership.TryGetProperty("owned", out var owned) && owned.ValueKind is JsonValueKind.True or JsonValueKind.False) return owned.GetBoolean();
        return item.TryGetProperty("owned", out var direct) && direct.ValueKind is JsonValueKind.True or JsonValueKind.False && direct.GetBoolean();
    }

    private static async Task<JsonDocument?> TryGetJsonAsync(HttpClient client, string path, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetAsync(path, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        catch (JsonException) { return null; }
    }

    private static async Task<JsonDocument?> GetJsonAsync(HttpClient client, string path, CancellationToken cancellationToken) => await TryGetJsonAsync(client, path, cancellationToken).ConfigureAwait(false);
    private static string? GetString(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static int? GetInt32(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.TryGetInt32(out var result) ? result : null;
    private static long? GetInt64(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.TryGetInt64(out var result) ? result : null;
    private static bool? GetBoolean(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
}
