using System.Globalization;

namespace SummonersVault.Infrastructure.League;

internal static class LeagueClientRoutes
{
    internal const string CurrentSummoner = "lol-summoner/v1/current-summoner";
    internal const string RegionLocale = "riotclient/region-locale";
    internal const string RankedStats = "lol-ranked/v1/current-ranked-stats";
    internal const string ChampionMastery = "lol-champion-mastery/v1/local-player/champion-mastery";
    internal const string EternalSummary = "lol-statstones/v2/player-summary-self";
    internal const string LootReady = "lol-loot/v1/ready";
    internal const string PlayerLoot = "lol-loot/v1/player-loot";
    internal const string RiotPointsCurrency = "RP";
    internal const string BlueEssenceCurrency = "lol_blue_essence";
    internal const string LegacyInfluencePointsCurrency = "IP";

    internal static IReadOnlyList<string> WalletSummaries { get; } =
    [
        "lol-login/v1/wallet",
        "lol-store/v1/wallet"
    ];

    internal static string ProfileIcon(int profileIconId) =>
        $"lol-game-data/assets/v1/profile-icons/{profileIconId.ToString(CultureInfo.InvariantCulture)}.jpg";

    internal static string WalletCurrency(string currencyType) =>
        $"lol-inventory/v1/wallet/{EncodePathSegment(currencyType)}";

    internal static string Champions(long summonerId) =>
        $"lol-champions/v1/inventories/{summonerId.ToString(CultureInfo.InvariantCulture)}/champions-minimal";

    internal static string Skins(long summonerId) =>
        $"lol-champions/v1/inventories/{summonerId.ToString(CultureInfo.InvariantCulture)}/skins-minimal";

    internal static string EternalDetails(int championId) =>
        $"lol-statstones/v2/player-statstones-self/{championId.ToString(CultureInfo.InvariantCulture)}";

    internal static string LatestMatches(string puuid) =>
        $"lol-match-history/v1/products/lol/{EncodePathSegment(puuid)}/matches?begIndex=0&endIndex=1";

    private static string EncodePathSegment(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return Uri.EscapeDataString(value);
    }
}
