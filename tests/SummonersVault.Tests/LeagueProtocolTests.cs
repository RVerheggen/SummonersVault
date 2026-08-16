using SummonersVault.Core.Models;
using SummonersVault.Infrastructure.League;
using Xunit;

namespace SummonersVault.Tests;

public sealed class LeagueProtocolTests
{
    [Fact]
    public void Routes_UseCentralizedStaticEndpoints()
    {
        Assert.Equal("lol-summoner/v1/current-summoner", LeagueClientRoutes.CurrentSummoner);
        Assert.Equal("riotclient/region-locale", LeagueClientRoutes.RegionLocale);
        Assert.Equal("lol-ranked/v1/current-ranked-stats", LeagueClientRoutes.RankedStats);
        Assert.Equal("lol-champion-mastery/v1/local-player/champion-mastery", LeagueClientRoutes.ChampionMastery);
        Assert.Equal("lol-statstones/v2/player-summary-self", LeagueClientRoutes.EternalSummary);
        Assert.Equal("lol-loot/v1/ready", LeagueClientRoutes.LootReady);
        Assert.Equal("lol-loot/v1/player-loot", LeagueClientRoutes.PlayerLoot);
    }

    [Fact]
    public void Routes_FormatNumericIdentifiersUsingStablePaths()
    {
        Assert.Equal("lol-game-data/assets/v1/profile-icons/29.jpg", LeagueClientRoutes.ProfileIcon(29));
        Assert.Equal("lol-champions/v1/inventories/123456/champions-minimal", LeagueClientRoutes.Champions(123456));
        Assert.Equal("lol-champions/v1/inventories/123456/skins-minimal", LeagueClientRoutes.Skins(123456));
        Assert.Equal("lol-statstones/v2/player-statstones-self/103", LeagueClientRoutes.EternalDetails(103));
    }

    [Fact]
    public void Routes_EncodeUserControlledPathSegments()
    {
        Assert.Equal("lol-inventory/v1/wallet/value%2F..%2Fsecret", LeagueClientRoutes.WalletCurrency("value/../secret"));
        Assert.Equal(
            "lol-match-history/v1/products/lol/player%2F..%2Fother/matches?begIndex=0&endIndex=1",
            LeagueClientRoutes.LatestMatches("player/../other"));
    }

    [Fact]
    public void Regions_ExposeAStableSupportedList()
    {
        Assert.Equal(
            ["EUW", "EUNE", "NA", "KR", "BR", "JP", "LAN", "LAS", "OCE", "TR"],
            LeagueRegion.Supported);
        Assert.Equal(LeagueRegion.Supported.Count, LeagueRegion.Supported.Distinct(StringComparer.Ordinal).Count());
        Assert.True(LeagueRegion.IsSupported("euw"));
        Assert.False(LeagueRegion.IsSupported("EUW1"));
    }
}
