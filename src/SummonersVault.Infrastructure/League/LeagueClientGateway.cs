using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Text;
using System.Text.Json;
using SummonersVault.Core.Abstractions;
using SummonersVault.Core.Models;

namespace SummonersVault.Infrastructure.League;

public sealed class LeagueClientGateway : ILeagueClientGateway
{
    private static readonly string DefaultInstallDirectory = Path.Combine(Environment.GetEnvironmentVariable("SystemDrive") ?? "C:", "Riot Games", "League of Legends");
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
        var riotName = GetString(root, "gameName") ?? GetString(root, "displayName") ?? "Unknown";
        var tag = GetString(root, "tagLine") ?? string.Empty;
        var iconId = GetInt32(root, "profileIconId");

        var region = "UNKNOWN";
        using (var regionJson = await TryGetJsonAsync(client, "riotclient/region-locale", cancellationToken).ConfigureAwait(false))
            if (regionJson is not null) region = GetString(regionJson.RootElement, "region")?.ToUpperInvariant() ?? region;

        var ranks = await FetchRanksAsync(client, cancellationToken).ConfigureAwait(false);
        var champions = summonerId.HasValue ? await FetchChampionsAsync(client, summonerId.Value, cancellationToken).ConfigureAwait(false) : null;
        var skins = summonerId.HasValue ? await FetchSkinsAsync(client, summonerId.Value, cancellationToken).ConfigureAwait(false) : null;
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
            ProfileIconId = iconId, ProfileIconBytes = icon, SummonerLevel = GetInt32(root, "summonerLevel"),
            Ranks = ranks, Champions = champions, Skins = skins, Match = match
        };
    }

    public Task<bool> LaunchAsync(string? configuredInstallDirectory, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _configuredInstallDirectory = configuredInstallDirectory ?? _configuredInstallDirectory;
        var executable = CandidateDirectories().Select(x => Path.Combine(x, "LeagueClient.exe")).FirstOrDefault(File.Exists);
        if (executable is null) return Task.FromResult(false);
        Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(executable)! });
        return Task.FromResult(true);
    }

    private async Task<LeagueLockfile?> FindLockfileAsync(CancellationToken cancellationToken)
    {
        foreach (var directory in CandidateDirectories())
        {
            var path = Path.Combine(directory, "lockfile");
            if (!File.Exists(path)) continue;
            try
            {
                var content = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                if (LeagueLockfile.TryParse(content, out var lockfile)) return lockfile;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return null;
    }

    private IEnumerable<string> CandidateDirectories()
    {
        if (!string.IsNullOrWhiteSpace(_configuredInstallDirectory)) yield return _configuredInstallDirectory;
        yield return DefaultInstallDirectory;
        Process[] processes = [];
        try { processes = Process.GetProcessesByName("LeagueClient"); } catch (InvalidOperationException) { }
        foreach (var process in processes)
        {
            using (process)
            {
                string? path = null;
                try { path = process.MainModule?.FileName; } catch { }
                if (path is not null) yield return Path.GetDirectoryName(path)!;
            }
        }
    }

    private static HttpClient CreateClient(LeagueLockfile lockfile)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (request, _, _, errors) =>
                request.RequestUri is { IsLoopback: true, Host: "127.0.0.1" } && errors is (SslPolicyErrors.None or SslPolicyErrors.RemoteCertificateChainErrors)
        };
        var client = new HttpClient(handler) { BaseAddress = lockfile.BaseUri, Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"riot:{lockfile.Password}")));
        return client;
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
            result.Add(new(queue, GetString(item, "tier") ?? "UNRANKED", GetString(item, "division") ?? string.Empty, GetInt32(item, "leaguePoints") ?? 0, GetInt32(item, "wins") ?? 0, GetInt32(item, "losses") ?? 0));
        }
        return result;
    }

    private static async Task<IReadOnlyList<OwnedChampion>?> FetchChampionsAsync(HttpClient client, long summonerId, CancellationToken cancellationToken)
    {
        using var json = await TryGetJsonAsync(client, $"lol-champions/v1/inventories/{summonerId}/champions-minimal", cancellationToken).ConfigureAwait(false);
        if (json is null || json.RootElement.ValueKind != JsonValueKind.Array) return null;
        return json.RootElement.EnumerateArray().Where(IsOwned).Select(x => new OwnedChampion(GetInt32(x, "id") ?? 0, GetString(x, "name") ?? "Unknown champion")).Where(x => x.ChampionId > 0).ToArray();
    }

    private static async Task<IReadOnlyList<OwnedSkin>?> FetchSkinsAsync(HttpClient client, long summonerId, CancellationToken cancellationToken)
    {
        using var json = await TryGetJsonAsync(client, $"lol-champions/v1/inventories/{summonerId}/skins-minimal", cancellationToken).ConfigureAwait(false);
        if (json is null || json.RootElement.ValueKind != JsonValueKind.Array) return null;
        return json.RootElement.EnumerateArray().Where(IsOwned).Select(x => new OwnedSkin(GetInt32(x, "id") ?? 0, GetInt32(x, "championId") ?? 0, GetString(x, "name") ?? "Unknown skin")).Where(x => x.SkinId > 0).ToArray();
    }

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
        catch (JsonException) { return null; }
    }

    private static async Task<JsonDocument?> GetJsonAsync(HttpClient client, string path, CancellationToken cancellationToken) => await TryGetJsonAsync(client, path, cancellationToken).ConfigureAwait(false);
    private static string? GetString(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static int? GetInt32(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.TryGetInt32(out var result) ? result : null;
    private static long? GetInt64(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.TryGetInt64(out var result) ? result : null;
}
