using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using SummonersVault.Application.Abstractions;
using SummonersVault.Core.Models;

namespace SummonersVault.Infrastructure.League;

public sealed class LeagueClientGateway(LeagueClientConnection connection) : ILeagueClientGateway, ILeagueClientConfiguration
{
    public void SetInstallDirectory(string? directory) => connection.SetInstallDirectory(directory);

    public async Task<LeagueClientStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        LeagueLockfile? lockfile = await connection.FindLockfileAsync(cancellationToken).ConfigureAwait(false);
        if (lockfile is null)
        {
            return new(false, false, "League Client is not running");
        }

        try
        {
            using HttpClient client = LeagueClientConnection.CreateAuthenticatedClient(lockfile);
            using HttpResponseMessage response = await client.GetAsync("lol-summoner/v1/current-summoner", cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? new(true, true, "League Client connected")
                : new(true, false, "League Client is waiting for sign-in");
        }
        catch (HttpRequestException) { return new(true, false, "League Client is starting"); }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(true, false, "League Client is starting"); }
    }

    public async Task<LeagueSnapshot> FetchCurrentSnapshotAsync(CancellationToken cancellationToken = default)
    {
        LeagueLockfile lockfile = await connection.FindLockfileAsync(cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("League Client is not running.");
        using HttpClient client = LeagueClientConnection.CreateAuthenticatedClient(lockfile);
        using JsonDocument summoner = await GetJsonAsync(client, "lol-summoner/v1/current-summoner", cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Sign into League Client before synchronizing.");
        JsonElement root = summoner.RootElement;
        string puuid = GetString(root, "puuid") ?? throw new InvalidDataException("League Client did not return a PUUID.");
        long? summonerId = GetInt64(root, "summonerId");
        int? summonerLevel = GetInt32(root, "summonerLevel");
        string riotName = GetString(root, "gameName") ?? GetString(root, "displayName") ?? "Unknown";
        string tag = GetString(root, "tagLine") ?? string.Empty;
        int? iconId = GetInt32(root, "profileIconId");

        string region = "UNKNOWN";
        using (JsonDocument? regionJson = await TryGetJsonAsync(client, "riotclient/region-locale", cancellationToken).ConfigureAwait(false))
        {
            if (regionJson is not null)
            {
                string normalizedRegion = LeagueRegion.Normalize(GetString(regionJson.RootElement, "region"));
                if (!string.IsNullOrEmpty(normalizedRegion))
                {
                    region = normalizedRegion;
                }
            }
        }

        Task<IReadOnlyList<RankSnapshot>?> ranksTask = FetchRanksAsync(client, cancellationToken);
        Task<LeagueWalletSnapshot?> walletTask = FetchWalletAsync(client, cancellationToken);
        Task<IReadOnlyList<OwnedChampion>?> championsTask = summonerId.HasValue
            ? FetchChampionsAsync(client, summonerId.Value, cancellationToken)
            : Task.FromResult<IReadOnlyList<OwnedChampion>?>(null);
        Task<IReadOnlyList<OwnedSkin>?> skinsTask = summonerId.HasValue
            ? FetchSkinsAsync(client, summonerId.Value, cancellationToken)
            : Task.FromResult<IReadOnlyList<OwnedSkin>?>(null);
        Task<MatchSnapshotResult> matchTask = FetchLatestMatchAsync(client, puuid, cancellationToken);
        Task<byte[]?> iconTask = FetchProfileIconAsync(client, iconId, cancellationToken);
        Task<IReadOnlyList<CraftingLootItem>?> craftingTask = FetchCraftingAfterInventoriesAsync(
            client,
            championsTask,
            skinsTask,
            cancellationToken);

        await Task.WhenAll(ranksTask, walletTask, championsTask, skinsTask, matchTask, iconTask, craftingTask).ConfigureAwait(false);

        return new LeagueSnapshot
        {
            Puuid = puuid,
            SummonerId = summonerId,
            RiotGameName = riotName,
            RiotTagLine = tag,
            Region = region,
            ProfileIconId = iconId,
            ProfileIconBytes = await iconTask.ConfigureAwait(false),
            SummonerLevel = summonerLevel,
            Wallet = await walletTask.ConfigureAwait(false),
            Ranks = await ranksTask.ConfigureAwait(false),
            Champions = await championsTask.ConfigureAwait(false),
            Skins = await skinsTask.ConfigureAwait(false),
            CraftingLoot = await craftingTask.ConfigureAwait(false),
            Match = await matchTask.ConfigureAwait(false)
        };
    }

    public async Task<ChampionProgressionSnapshot> FetchChampionProgressionAsync(CancellationToken cancellationToken = default)
    {
        LeagueLockfile lockfile = await connection.FindLockfileAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("League Client is not running.");
        using HttpClient client = LeagueClientConnection.CreateAuthenticatedClient(lockfile);
        using JsonDocument summoner = await GetJsonAsync(client, "lol-summoner/v1/current-summoner", cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Sign into League Client before synchronizing.");
        string puuid = GetString(summoner.RootElement, "puuid")
            ?? throw new InvalidDataException("League Client did not return a PUUID.");

        Task<IReadOnlyList<ChampionMastery>?> masteryTask = FetchChampionMasteriesAsync(client, cancellationToken);
        Task<ChampionEternalsSnapshot?> eternalsTask = FetchChampionEternalsAsync(client, cancellationToken);
        await Task.WhenAll(masteryTask, eternalsTask).ConfigureAwait(false);

        return new ChampionProgressionSnapshot
        {
            Puuid = puuid,
            ChampionMasteries = await masteryTask.ConfigureAwait(false),
            ChampionEternals = await eternalsTask.ConfigureAwait(false)
        };
    }

    public async Task<byte[]?> FetchAssetAsync(string assetPath, CancellationToken cancellationToken = default)
    {
        if (!IsSafeAssetPath(assetPath))
        {
            return null;
        }

        LeagueLockfile? lockfile = await connection.FindLockfileAsync(cancellationToken).ConfigureAwait(false);
        if (lockfile is null)
        {
            return null;
        }

        try
        {
            using HttpClient client = LeagueClientConnection.CreateAuthenticatedClient(lockfile);
            using HttpResponseMessage response = await client.GetAsync(assetPath.TrimStart('/'), cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 8 * 1024 * 1024)
            {
                return null;
            }

            return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException) { return null; }
    }

    public Task<bool> LaunchAsync(string? configuredInstallDirectory, CancellationToken cancellationToken = default) =>
        connection.LaunchAsync(configuredInstallDirectory, cancellationToken);

    internal static async Task<string> ReadLockfileAsync(string path, CancellationToken cancellationToken = default)
    {
        return await LeagueClientConnection.ReadLockfileAsync(path, cancellationToken).ConfigureAwait(false);
    }

    internal static bool IsLeagueLoopbackRequest(Uri? uri) =>
        LeagueClientConnection.IsLeagueLoopbackRequest(uri);

    internal static bool IsInventoryPayloadReady(JsonElement element) =>
        element.ValueKind == JsonValueKind.Array;

    internal static LeagueWalletSnapshot? ParseWallet(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object
            ? new LeagueWalletSnapshot(GetWalletValue(element, "rp"), GetWalletValue(element, "lol_blue_essence", "ip", "blueEssence", "be"))
            : null;

    private static async Task<IReadOnlyList<CraftingLootItem>?> FetchCraftingAfterInventoriesAsync(
        HttpClient client,
        Task<IReadOnlyList<OwnedChampion>?> championsTask,
        Task<IReadOnlyList<OwnedSkin>?> skinsTask,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<OwnedChampion>? champions = await championsTask.ConfigureAwait(false);
        IReadOnlyList<OwnedSkin>? skins = await skinsTask.ConfigureAwait(false);
        return await FetchCraftingLootAsync(client, champions, skins, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<byte[]?> FetchProfileIconAsync(HttpClient client, int? iconId, CancellationToken cancellationToken)
    {
        if (!iconId.HasValue)
        {
            return null;
        }

        try
        {
            using HttpResponseMessage response = await client.GetAsync(
                $"lol-game-data/assets/v1/profile-icons/{iconId.Value}.jpg",
                cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false)
                : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private static async Task<LeagueWalletSnapshot?> FetchWalletAsync(HttpClient client, CancellationToken cancellationToken)
    {
        LeagueWalletSnapshot? partial = null;
        foreach (string? path in new[] { "lol-login/v1/wallet", "lol-store/v1/wallet" })
        {
            using JsonDocument? json = await TryGetJsonAsync(client, path, cancellationToken).ConfigureAwait(false);
            if (json is null)
            {
                continue;
            }

            LeagueWalletSnapshot? wallet = ParseWallet(json.RootElement);
            if (wallet is null)
            {
                continue;
            }

            partial = MergeWallet(partial, wallet);
            if (partial is { RiotPoints: not null, BlueEssence: not null })
            {
                return partial;
            }
        }

        long? riotPoints = partial?.RiotPoints
            ?? await FetchWalletCurrencyAsync(client, "RP", cancellationToken).ConfigureAwait(false);
        long? blueEssence = partial?.BlueEssence
            ?? await FetchWalletCurrencyAsync(client, "lol_blue_essence", cancellationToken).ConfigureAwait(false)
            ?? await FetchWalletCurrencyAsync(client, "IP", cancellationToken).ConfigureAwait(false);

        if (riotPoints.HasValue || blueEssence.HasValue)
        {
            return new LeagueWalletSnapshot(riotPoints, blueEssence);
        }

        return partial;
    }

    private static LeagueWalletSnapshot MergeWallet(LeagueWalletSnapshot? current, LeagueWalletSnapshot incoming) =>
        new(current?.RiotPoints ?? incoming.RiotPoints, current?.BlueEssence ?? incoming.BlueEssence);

    private static async Task<long?> FetchWalletCurrencyAsync(
        HttpClient client,
        string currencyType,
        CancellationToken cancellationToken)
    {
        using JsonDocument? json = await TryGetJsonAsync(
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
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!names.Any(name => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            long? value = TryGetNumericValue(property.Value);
            if (value.HasValue)
            {
                return value;
            }
        }
        return null;
    }

    private static long? TryGetNumericValue(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out long numeric))
        {
            return numeric;
        }

        if (element.ValueKind == JsonValueKind.String
            && long.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out numeric))
        {
            return numeric;
        }

        return null;
    }

    private static async Task<IReadOnlyList<RankSnapshot>?> FetchRanksAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using JsonDocument? json = await TryGetJsonAsync(client, "lol-ranked/v1/current-ranked-stats", cancellationToken).ConfigureAwait(false);
        if (json is null)
        {
            return null;
        }

        JsonElement element = json.RootElement;
        if (element.TryGetProperty("queues", out JsonElement queues))
        {
            element = queues;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<RankSnapshot>();
        foreach (JsonElement item in element.EnumerateArray())
        {
            string? queue = GetString(item, "queueType");
            if (string.IsNullOrWhiteSpace(queue))
            {
                continue;
            }

            if (queue.Equals("NONE", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            result.Add(new(queue, GetString(item, "tier") ?? "UNRANKED", GetString(item, "division") ?? string.Empty,
                GetInt32(item, "leaguePoints") ?? 0, GetInt32(item, "wins") ?? 0, GetInt32(item, "losses") ?? 0,
                GetBoolean(item, "isProvisional") ?? false, GetInt32(item, "provisionalGamesRemaining"),
                GetString(item, "ratedTier"), GetInt32(item, "ratedRating")));
        }
        return result;
    }

    private static async Task<IReadOnlyList<OwnedChampion>?> FetchChampionsAsync(HttpClient client, long summonerId, CancellationToken cancellationToken)
    {
        using JsonDocument? json = await TryGetJsonAsync(client, $"lol-champions/v1/inventories/{summonerId}/champions-minimal", cancellationToken).ConfigureAwait(false);
        if (json is null || !IsInventoryPayloadReady(json.RootElement))
        {
            return null;
        }

        return ParseOwnedChampions(json.RootElement);
    }

    internal static IReadOnlyList<OwnedChampion> ParseOwnedChampions(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. element.EnumerateArray().Where(IsOwned).Select(item =>
        {
            int championId = GetInt32(item, "id") ?? 0;
            string? alias = GetString(item, "alias");
            return new OwnedChampion(championId, GetString(item, "name") ?? "Unknown champion",
                GetString(item, "baseSplashPath"), GetString(item, "squarePortraitPath"), alias,
                ClassifyChampionVariant(championId, alias));
        }).Where(champion => champion.ChampionId > 0)];
    }

    internal static ChampionVariant ClassifyChampionVariant(int championId, string? alias)
    {
        if (alias?.StartsWith("Jade_", StringComparison.OrdinalIgnoreCase) == true || championId is >= 60000 and <= 60999)
        {
            return ChampionVariant.LeagueClassic;
        }

        return string.IsNullOrWhiteSpace(alias) || !alias.Contains('_', StringComparison.Ordinal)
            ? ChampionVariant.Current
            : ChampionVariant.Unknown;
    }

    private static async Task<IReadOnlyList<ChampionMastery>?> FetchChampionMasteriesAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using JsonDocument? json = await TryGetJsonAsync(client, "lol-champion-mastery/v1/local-player/champion-mastery", cancellationToken).ConfigureAwait(false);
        return json is null ? null : ParseChampionMasteries(json.RootElement);
    }

    internal static IReadOnlyList<ChampionMastery> ParseChampionMasteries(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<ChampionMastery>();
        foreach (JsonElement item in element.EnumerateArray())
        {
            int championId = GetInt32(item, "championId") ?? 0;
            if (championId <= 0)
            {
                continue;
            }

            result.Add(new ChampionMastery(
                championId,
                GetInt32(item, "championLevel") ?? 0,
                GetInt64(item, "championPoints") ?? 0,
                GetInt64(item, "championPointsSinceLastLevel") ?? 0,
                GetInt64(item, "championPointsUntilNextLevel") ?? 0,
                GetInt32(item, "championSeasonMilestone") ?? 0,
                GetString(item, "highestGrade"),
                ParseEpoch(GetInt64(item, "lastPlayTime")),
                GetInt32(item, "markRequiredForNextLevel") ?? 0,
                GetStringArray(item, "milestoneGrades"),
                GetInt32(item, "tokensEarned") ?? 0));
        }

        return result;
    }

    private static async Task<ChampionEternalsSnapshot?> FetchChampionEternalsAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using JsonDocument? summaryJson = await TryGetJsonAsync(client, "lol-statstones/v2/player-summary-self", cancellationToken).ConfigureAwait(false);
        if (summaryJson is null)
        {
            return null;
        }

        IReadOnlyList<ChampionEternalSummary> summaries = ParseEternalSummaries(summaryJson.RootElement);
        Dictionary<(int ChampionId, string Name), ChampionEternalSet> summarySets = ParseEternalSetSummaries(summaryJson.RootElement)
            .ToDictionary(set => (set.ChampionId, set.Name), set => set);
        ChampionEternalSummary[] owned = [.. summaries.Where(summary => summary.StonesOwned > 0)];
        using var concurrency = new SemaphoreSlim(4, 4);
        Task<EternalDetailResult>[] requests = [.. owned.Select(async summary =>
        {
            await concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using JsonDocument? detailJson = await TryGetJsonAsync(client, $"lol-statstones/v2/player-statstones-self/{summary.ChampionId}", cancellationToken).ConfigureAwait(false);
                if (detailJson is null)
                {
                    return EternalDetailResult.Failed(summary.ChampionId);
                }

                EternalDetailResult parsed = ParseEternalDetails(summary.ChampionId, detailJson.RootElement);
                return parsed.Sets.Count == 0 && summary.StonesOwned > 0 ? EternalDetailResult.Failed(summary.ChampionId) : parsed;
            }
            finally
            {
                concurrency.Release();
            }
        })];
        EternalDetailResult[] details = await Task.WhenAll(requests).ConfigureAwait(false);
        int[] successfulIds = [.. details.Where(detail => detail.Succeeded).Select(detail => detail.ChampionId)];
        ChampionEternalSet[] mergedSets = [.. details.Where(detail => detail.Succeeded).SelectMany(detail => detail.Sets).Select(set =>
            summarySets.TryGetValue((set.ChampionId, set.Name), out ChampionEternalSet? summarySet)
                ? set with
                {
                    MilestonesPassed = summarySet.MilestonesPassed,
                    StonesAvailable = summarySet.StonesAvailable,
                    StonesIlluminated = summarySet.StonesIlluminated,
                    StonesOwned = summarySet.StonesOwned
                }
                : set)];
        return new ChampionEternalsSnapshot(
            summaries,
            mergedSets,
            [.. details.Where(detail => detail.Succeeded).SelectMany(detail => detail.Eternals)],
            successfulIds.ToHashSet(),
            details.All(detail => detail.Succeeded));
    }

    internal static IReadOnlyList<ChampionEternalSummary> ParseEternalSummaries(JsonElement element)
    {
        JsonElement champions = element.ValueKind == JsonValueKind.Object && element.TryGetProperty("championSummaries", out JsonElement summaries)
            ? summaries
            : element;
        if (champions.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. champions.EnumerateArray().Select(item => new ChampionEternalSummary(
            GetInt32(item, "championId") ?? 0,
            GetInt32(item, "milestonesPassed") ?? 0,
            GetInt32(item, "stonesAvailable") ?? 0,
            GetInt32(item, "stonesIlluminated") ?? 0,
            GetInt32(item, "stonesOwned") ?? 0)).Where(item => item.ChampionId > 0)];
    }

    internal static IReadOnlyList<ChampionEternalSet> ParseEternalSetSummaries(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<ChampionEternalSet>();
        foreach (JsonElement champion in element.EnumerateArray())
        {
            int championId = GetInt32(champion, "championId") ?? 0;
            if (championId <= 0 || !champion.TryGetProperty("sets", out JsonElement sets) || sets.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            result.AddRange(sets.EnumerateArray().Select(set => new ChampionEternalSet(
                championId,
                0,
                GetString(set, "name") ?? "Eternals",
                GetInt32(set, "milestonesPassed") ?? 0,
                GetInt32(set, "stonesAvailable") ?? 0,
                GetInt32(set, "stonesIlluminated") ?? 0,
                GetInt32(set, "stonesOwned") ?? 0)));
        }

        return result;
    }

    internal static EternalDetailResult ParseEternalDetails(int championId, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            return EternalDetailResult.Failed(championId);
        }

        var sets = new List<ChampionEternalSet>();
        var eternals = new List<ChampionEternal>();
        foreach (JsonElement setElement in element.EnumerateArray())
        {
            int setId = GetInt32(setElement, "itemId") ?? GetInt32(setElement, "itemInstanceID") ?? 0;
            sets.Add(new ChampionEternalSet(championId, setId, GetString(setElement, "name") ?? "Eternals",
                GetInt32(setElement, "milestonesPassed") ?? 0, GetInt32(setElement, "stonesAvailable") ?? 0,
                GetInt32(setElement, "stonesIlluminated") ?? 0, GetInt32(setElement, "stonesOwned") ?? 0));
            if (!setElement.TryGetProperty("statstones", out JsonElement stones) || stones.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement stone in stones.EnumerateArray())
            {
                string? statstoneId = GetScalarString(stone, "statstoneId") ?? GetString(stone, "contentId");
                if (string.IsNullOrWhiteSpace(statstoneId) || !IsOwnedEternal(stone))
                {
                    continue;
                }

                JsonElement record = stone.TryGetProperty("playerRecord", out JsonElement playerRecord) && playerRecord.ValueKind == JsonValueKind.Object
                    ? playerRecord
                    : default;
                eternals.Add(new ChampionEternal(championId, setId, statstoneId, GetString(stone, "name") ?? "Eternal",
                    GetString(stone, "description"), GetString(stone, "category"), GetDouble(record, "value") ?? 0,
                    GetString(stone, "formattedValue"), GetInt32(record, "milestoneLevel") ?? 0,
                    GetString(stone, "formattedMilestoneLevel"), GetDouble(stone, "nextMilestone"),
                    GetDouble(record, "personalBest"), GetString(stone, "formattedPersonalBest"),
                    GetBoolean(stone, "isComplete") ?? false, GetBoolean(stone, "isEpic") ?? false,
                    GetBoolean(stone, "isFeatured") ?? false, GetBoolean(stone, "isRetired") ?? false,
                    GetString(stone, "imageUrl")));
            }
        }

        return new EternalDetailResult(championId, true, sets, eternals);
    }

    private static bool IsOwnedEternal(JsonElement stone)
    {
        if (stone.TryGetProperty("playerRecord", out JsonElement record) && record.ValueKind == JsonValueKind.Object)
        {
            return GetBoolean(record, "entitled") != false;
        }

        return false;
    }

    private static async Task<IReadOnlyList<OwnedSkin>?> FetchSkinsAsync(HttpClient client, long summonerId, CancellationToken cancellationToken)
    {
        using JsonDocument? json = await TryGetJsonAsync(client, $"lol-champions/v1/inventories/{summonerId}/skins-minimal", cancellationToken).ConfigureAwait(false);
        if (json is null || !IsInventoryPayloadReady(json.RootElement))
        {
            return null;
        }

        IEnumerable<OwnedSkin> skins = json.RootElement.EnumerateArray()
            .Where(IsOwned)
            .Where(x => GetBoolean(x, "isBase") != true)
            .Select(x => new OwnedSkin(GetInt32(x, "id") ?? 0, GetInt32(x, "championId") ?? 0, GetString(x, "name") ?? "Unknown skin",
                GetString(x, "splashPath"), GetString(x, "tilePath")));
        return OwnedSkinRules.Normalize(skins);
    }

    private static async Task<IReadOnlyList<CraftingLootItem>?> FetchCraftingLootAsync(HttpClient client, IReadOnlyList<OwnedChampion>? champions, IReadOnlyList<OwnedSkin>? skins, CancellationToken cancellationToken)
    {
        using JsonDocument? readyJson = await TryGetJsonAsync(client, "lol-loot/v1/ready", cancellationToken).ConfigureAwait(false);
        if (readyJson is null || !ParseLootReady(readyJson.RootElement))
        {
            return null;
        }

        using JsonDocument? json = await TryGetJsonAsync(client, "lol-loot/v1/player-loot", cancellationToken).ConfigureAwait(false);
        if (json is null)
        {
            return null;
        }

        return EnrichCraftingNames(ParseCraftingLoot(json.RootElement), champions, skins);
    }

    internal static bool ParseLootReady(JsonElement element) => element.ValueKind == JsonValueKind.True
        || element.ValueKind == JsonValueKind.Object && GetBoolean(element, "ready") == true;

    internal static IReadOnlyList<CraftingLootItem> ParseCraftingLoot(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<CraftingLootItem>();
        foreach (JsonElement item in element.EnumerateArray())
        {
            int count = GetInt32(item, "count") ?? 0;
            string? lootId = GetString(item, "lootId");
            if (count <= 0 || string.IsNullOrWhiteSpace(lootId))
            {
                continue;
            }

            string lootName = FirstNonBlank(GetString(item, "lootName"), lootId);
            string type = GetString(item, "type") ?? "Other";
            string display = CategorizeLoot(type, lootName, GetString(item, "displayCategories"));
            string localizedName = CurrencyDisplayName(lootId, lootName)
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
        string value = $"{displayCategory} {type} {name}";
        if (value.Contains("CURRENCY", StringComparison.OrdinalIgnoreCase))
        {
            return "Currencies";
        }

        if (value.Contains("CHAMPION", StringComparison.OrdinalIgnoreCase))
        {
            return "Champion shards";
        }

        if (value.Contains("SKIN", StringComparison.OrdinalIgnoreCase))
        {
            return "Skin shards";
        }

        if (value.Contains("MATERIAL", StringComparison.OrdinalIgnoreCase) || value.Contains("CHEST", StringComparison.OrdinalIgnoreCase) || value.Contains("KEY", StringComparison.OrdinalIgnoreCase) || value.Contains("TOKEN", StringComparison.OrdinalIgnoreCase))
        {
            return "Materials";
        }

        return "Other";
    }

    private static string? CurrencyDisplayName(string lootId, string lootName)
    {
        string value = $"{lootId} {lootName}";
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

    private static CraftingLootItem[] EnrichCraftingNames(IReadOnlyList<CraftingLootItem> items, IReadOnlyList<OwnedChampion>? champions, IReadOnlyList<OwnedSkin>? skins)
    {
        Dictionary<int, string> championNames = champions?.ToDictionary(x => x.ChampionId, x => x.Name) ?? [];
        Dictionary<int, string> skinNames = skins?.ToDictionary(x => x.SkinId, x => x.Name) ?? [];
        return [.. items.Select(item =>
        {
            if (!string.IsNullOrWhiteSpace(item.LocalizedName)
                && !item.LocalizedName.Equals(item.LootId, StringComparison.OrdinalIgnoreCase)
                && !item.LocalizedName.Equals(item.LootName, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }

            if (!int.TryParse(item.ReferenceId, NumberStyles.Integer, CultureInfo.InvariantCulture, out int referenceId))
            {
                return item;
            }

            if (item.DisplayCategory == "Champion shards" && championNames.TryGetValue(referenceId, out string? championName))
            {
                return item with { LocalizedName = $"{championName} shard" };
            }

            if (item.DisplayCategory == "Skin shards" && skinNames.TryGetValue(referenceId, out string? skinName))
            {
                return item with { LocalizedName = $"{skinName} shard" };
            }

            return item;
        })];
    }

    private static string FirstNonBlank(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "Unknown item";
    private static string? GetScalarString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ValueKind == JsonValueKind.Number ? value.GetRawText() : null;
    }

    private static DateTimeOffset? ParseEpoch(long? value)
    {
        if (!value.HasValue || value <= 0)
        {
            return null;
        }

        try { return value > 10_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(value.Value) : DateTimeOffset.FromUnixTimeSeconds(value.Value); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    internal static bool IsSafeAssetPath(string? path) => !string.IsNullOrWhiteSpace(path)
        && path.StartsWith('/') && !path.Contains("..", StringComparison.Ordinal) && !path.Contains('\\')
        && !Uri.TryCreate(path, UriKind.Absolute, out _);

    private static async Task<MatchSnapshotResult> FetchLatestMatchAsync(HttpClient client, string puuid, CancellationToken cancellationToken)
    {
        using JsonDocument? json = await TryGetJsonAsync(client, $"lol-match-history/v1/products/lol/{Uri.EscapeDataString(puuid)}/matches?begIndex=0&endIndex=1", cancellationToken).ConfigureAwait(false);
        if (json is null)
        {
            return MatchSnapshotResult.Failed;
        }

        return LeagueMatchHistoryParser.Parse(json.RootElement);
    }

    private static bool IsOwned(JsonElement item)
    {
        if (item.TryGetProperty("ownership", out JsonElement ownership) && ownership.TryGetProperty("owned", out JsonElement owned) && owned.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return owned.GetBoolean();
        }

        return item.TryGetProperty("owned", out JsonElement direct) && direct.ValueKind is JsonValueKind.True or JsonValueKind.False && direct.GetBoolean();
    }

    private static async Task<JsonDocument?> TryGetJsonAsync(HttpClient client, string path, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await client.GetAsync(path, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        catch (JsonException) { return null; }
    }

    private static async Task<JsonDocument?> GetJsonAsync(HttpClient client, string path, CancellationToken cancellationToken) => await TryGetJsonAsync(client, path, cancellationToken).ConfigureAwait(false);
    private static string? GetString(JsonElement element, string property) => element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static int? GetInt32(JsonElement element, string property) => element.TryGetProperty(property, out JsonElement value) && value.TryGetInt32(out int result) ? result : null;
    private static long? GetInt64(JsonElement element, string property) => element.TryGetProperty(property, out JsonElement value) && value.TryGetInt64(out long result) ? result : null;
    private static bool? GetBoolean(JsonElement element, string property) => element.TryGetProperty(property, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
    private static double? GetDouble(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double result))
        {
            return result;
        }

        return value.ValueKind == JsonValueKind.String
            && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out result)
                ? result
                : null;
    }
    private static IReadOnlyList<string> GetStringArray(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.Array
            ? [.. value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).Where(item => !string.IsNullOrWhiteSpace(item))]
            : [];
}

internal sealed record EternalDetailResult(
    int ChampionId,
    bool Succeeded,
    IReadOnlyList<ChampionEternalSet> Sets,
    IReadOnlyList<ChampionEternal> Eternals)
{
    public static EternalDetailResult Failed(int championId) => new(championId, false, [], []);
}
