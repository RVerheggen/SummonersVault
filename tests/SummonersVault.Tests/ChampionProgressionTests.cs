using System.Text.Json;
using SummonersVault.App.ViewModels;
using SummonersVault.Application.Abstractions;
using SummonersVault.Application.Settings;
using SummonersVault.Core.Models;
using SummonersVault.Infrastructure.League;
using Xunit;

namespace SummonersVault.Tests;

public sealed class ChampionProgressionTests
{
    [Fact]
    public void ChampionVariant_SeparatesCurrentClassicAndUnknownChampions()
    {
        Assert.Equal(ChampionVariant.Current, LeagueClientGateway.ClassifyChampionVariant(103, "Ahri"));
        Assert.Equal(ChampionVariant.LeagueClassic, LeagueClientGateway.ClassifyChampionVariant(60103, "Jade_Ahri"));
        Assert.Equal(ChampionVariant.LeagueClassic, LeagueClientGateway.ClassifyChampionVariant(60001, null));
        Assert.Equal(ChampionVariant.Unknown, LeagueClientGateway.ClassifyChampionVariant(70001, "Future_Ahri"));
    }

    [Fact]
    public void MasteryParser_PreservesProgressAndTextualMilestones()
    {
        using JsonDocument json = JsonDocument.Parse("""
            [{"championId":103,"championLevel":7,"championPoints":123456,"championPointsSinceLastLevel":2345,
              "championPointsUntilNextLevel":7655,"championSeasonMilestone":4,"highestGrade":"S+",
              "lastPlayTime":1786309200000,"markRequiredForNextLevel":3,"milestoneGrades":["S","A"],"tokensEarned":2}]
            """);

        ChampionMastery mastery = Assert.Single(LeagueClientGateway.ParseChampionMasteries(json.RootElement));

        Assert.Equal(103, mastery.ChampionId);
        Assert.Equal(7, mastery.Level);
        Assert.Equal(123456, mastery.Points);
        Assert.Equal("S+", mastery.HighestGrade);
        Assert.Equal(["S", "A"], mastery.MilestoneGrades);
        Assert.NotNull(mastery.LastPlayAtUtc);
    }

    [Fact]
    public void EternalParsers_KeepOnlyOwnedProgressIncludingRetiredEternals()
    {
        using JsonDocument summaryJson = JsonDocument.Parse("""
            [{"championId":103,"milestonesPassed":12,"stonesAvailable":6,"stonesIlluminated":2,"stonesOwned":3,
              "sets":[{"name":"Series 1","milestonesPassed":8,"stonesAvailable":3,"stonesIlluminated":1,"stonesOwned":2}]}]
            """);
        ChampionEternalSummary summary = Assert.Single(LeagueClientGateway.ParseEternalSummaries(summaryJson.RootElement));
        Assert.Equal(3, summary.StonesOwned);
        ChampionEternalSet setSummary = Assert.Single(LeagueClientGateway.ParseEternalSetSummaries(summaryJson.RootElement));
        Assert.Equal(1, setSummary.StonesIlluminated);

        using JsonDocument detailJson = JsonDocument.Parse("""
            [{"itemId":1,"name":"Series 1","milestonesPassed":12,"stonesAvailable":3,"stonesIlluminated":2,"stonesOwned":2,
              "statstones":[
                {"statstoneId":"owned","name":"Legacy Dash","description":"Track dashes","formattedValue":"42","formattedMilestoneLevel":"5",
                 "nextMilestone":"50","isComplete":true,"isEpic":false,"isFeatured":true,"isRetired":true,
                 "playerRecord":{"entitled":true,"value":42,"milestoneLevel":5,"personalBest":8}},
                {"statstoneId":"not-owned","name":"Locked","playerRecord":{"entitled":false,"value":0,"milestoneLevel":0}}
              ]}]
            """);

        EternalDetailResult details = LeagueClientGateway.ParseEternalDetails(103, detailJson.RootElement);

        Assert.True(details.Succeeded);
        Assert.Single(details.Sets);
        ChampionEternal eternal = Assert.Single(details.Eternals);
        Assert.Equal("owned", eternal.StatstoneId);
        Assert.True(eternal.IsRetired);
        Assert.True(eternal.IsComplete);
        Assert.Equal(50, eternal.NextMilestone);
    }

    [Fact]
    public void ChampionGallery_SortsUnknownMasteryLastInBothDirections()
    {
        var account = new VaultAccount();
        account.Champions.AddRange([
            new(1, "Alpha"),
            new(2, "Beta"),
            new(3, "Gamma")
        ]);
        account.ChampionMasteries.AddRange([
            new(1, 4, 400, 0, 0, 0, null, null, 0, [], 0),
            new(2, 7, 700, 0, 0, 0, null, null, 0, [], 0)
        ]);
        using var viewModel = new AccountDetailsViewModel(account, new NullArtworkService(), new AppSettings());

        viewModel.ChampionSort = "Mastery level";
        viewModel.ChampionSortDirection = "Descending";
        Assert.Equal(["Beta", "Alpha", "Gamma"], viewModel.Champions.Select(item => item.Name));

        viewModel.ChampionSortDirection = "Ascending";
        Assert.Equal(["Alpha", "Beta", "Gamma"], viewModel.Champions.Select(item => item.Name));
    }

    [Theory]
    [InlineData(800, 2, 2)]
    [InlineData(1000, 3, 3)]
    [InlineData(1400, 4, 3)]
    public void AccountDetailGalleries_UseAdaptiveColumnCount(double width, int expectedColumns, int expectedCraftingColumns)
    {
        var account = new VaultAccount();
        account.Champions.AddRange([
            new(1, "Champion One"),
            new(2, "Champion Two"),
            new(3, "Champion Three"),
            new(4, "Champion Four"),
            new(5, "Champion Five")
        ]);
        account.Skins.AddRange([
            new(1001, 1, "Skin One"),
            new(1002, 2, "Skin Two"),
            new(1003, 3, "Skin Three"),
            new(1004, 4, "Skin Four"),
            new(1005, 5, "Skin Five")
        ]);
        account.Ranks.AddRange([
            new("QUEUE_ONE", "GOLD", "IV", 1, 1, 1),
            new("QUEUE_TWO", "GOLD", "IV", 2, 2, 2),
            new("QUEUE_THREE", "GOLD", "IV", 3, 3, 3),
            new("QUEUE_FOUR", "GOLD", "IV", 4, 4, 4),
            new("QUEUE_FIVE", "GOLD", "IV", 5, 5, 5)
        ]);
        account.LootItems.AddRange([
            CreateLootItem("loot-one", "Loot One"),
            CreateLootItem("loot-two", "Loot Two"),
            CreateLootItem("loot-three", "Loot Three"),
            CreateLootItem("loot-four", "Loot Four"),
            CreateLootItem("loot-five", "Loot Five")
        ]);
        using var viewModel = new AccountDetailsViewModel(account, new NullArtworkService(), new AppSettings());
        viewModel.UpdateGalleryViewport(width);

        Assert.Equal(expectedColumns, viewModel.GalleryColumnCount);
        Assert.Equal(expectedCraftingColumns, viewModel.CraftingColumnCount);
        Assert.Equal(expectedColumns, viewModel.ChampionRows[0].Items.Count);
        Assert.Equal((int)Math.Ceiling(5d / expectedColumns), viewModel.ChampionRows.Count);
        Assert.Equal(expectedColumns, viewModel.SkinRows[0].Items.Count);
        Assert.Equal((int)Math.Ceiling(5d / expectedColumns), viewModel.SkinRows.Count);
        Assert.Equal(expectedColumns, viewModel.RankRows[0].Items.Count);
        Assert.Equal((int)Math.Ceiling(5d / expectedColumns), viewModel.RankRows.Count);
        Assert.Equal(expectedCraftingColumns, viewModel.LootRows[0].Items.Count);
        Assert.Equal((int)Math.Ceiling(5d / expectedCraftingColumns), viewModel.LootRows.Count);
    }

    [Fact]
    public void AccountDetailTabs_ExplainUnsynchronizedEmptySnapshots()
    {
        using var viewModel = new AccountDetailsViewModel(new VaultAccount(), new NullArtworkService(), new AppSettings());

        Assert.True(viewModel.IsChampionEmptyStateVisible);
        Assert.True(viewModel.IsSkinEmptyStateVisible);
        Assert.True(viewModel.IsRankedEmptyStateVisible);
        Assert.True(viewModel.IsCraftingEmptyStateVisible);
        Assert.Equal("Champions not synchronized", viewModel.ChampionEmptyTitle);
        Assert.Equal("Skins not synchronized", viewModel.SkinEmptyTitle);
        Assert.Equal("Ranked data not synchronized", viewModel.RankedEmptyTitle);
        Assert.Equal("Crafting not synchronized", viewModel.CraftingEmptyTitle);
        Assert.Equal("Not synced", viewModel.OrangeEssence);
        Assert.Equal("Not synced", viewModel.MythicEssence);
    }

    [Fact]
    public void AccountDetailTabs_DistinguishSynchronizedEmptySnapshots()
    {
        var account = new VaultAccount();
        account.SyncCategories.AddRange(
        [
            new(SnapshotCategory.Champions, SnapshotState.Current, null, null),
            new(SnapshotCategory.Skins, SnapshotState.Current, null, null),
            new(SnapshotCategory.Ranked, SnapshotState.Current, null, null),
            new(SnapshotCategory.Crafting, SnapshotState.Current, null, null)
        ]);
        using var viewModel = new AccountDetailsViewModel(account, new NullArtworkService(), new AppSettings());

        Assert.Equal("No champions in this collection", viewModel.ChampionEmptyTitle);
        Assert.Equal("No owned skins found", viewModel.SkinEmptyTitle);
        Assert.Equal("No ranked queues found", viewModel.RankedEmptyTitle);
        Assert.Equal("Crafting inventory is empty", viewModel.CraftingEmptyTitle);
        Assert.Equal("0", viewModel.OrangeEssence);
        Assert.Equal("0", viewModel.MythicEssence);
    }

    private static CraftingLootItem CreateLootItem(string lootId, string name) =>
        new(lootId, name, "MATERIAL", "Materials", name, null, 1, null, null, null, null, null, null, null, null);

    private sealed class NullArtworkService : IArtworkService
    {
        public Task<string?> ResolveAsync(string? assetPath, bool allowCommunityDragon, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public long GetCacheSizeBytes() => 0;
    }
}
