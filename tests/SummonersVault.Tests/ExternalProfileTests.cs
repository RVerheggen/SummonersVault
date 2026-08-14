using System.ComponentModel;
using System.Diagnostics;
using SummonersVault.App.Services;
using SummonersVault.App.ViewModels;
using SummonersVault.Application.Abstractions;
using SummonersVault.Application.ExternalProfiles;
using SummonersVault.Application.Settings;
using SummonersVault.Core.Models;
using SummonersVault.Infrastructure.Settings;
using SummonersVault.Infrastructure.Storage;
using Xunit;

namespace SummonersVault.Tests;

public sealed class ExternalProfileTests
{
    public static TheoryData<ExternalProfileProvider, string> ProviderAddresses => new()
    {
        { ExternalProfileProvider.OpGg, "https://op.gg/lol/summoners/euw/Summoner-Tag" },
        { ExternalProfileProvider.DeepLol, "https://www.deeplol.gg/summoner/euw/Summoner-Tag" },
        { ExternalProfileProvider.DpmLol, "https://dpm.lol/Summoner-Tag" },
        { ExternalProfileProvider.LeagueOfGraphs, "https://www.leagueofgraphs.com/summoner/euw/Summoner-Tag" }
    };

    [Theory]
    [MemberData(nameof(ProviderAddresses))]
    public void ProfileLinkBuilderUsesFixedProviderAddress(
        ExternalProfileProvider provider,
        string expectedAddress)
    {
        bool built = ExternalProfileLinkBuilder.TryBuild(provider, " Summoner ", " Tag ", "EUW1", out ExternalProfileLink? profileLink);

        Assert.True(built);
        Assert.NotNull(profileLink);
        Assert.Equal(expectedAddress, profileLink.Uri.AbsoluteUri);
        Assert.True(ExternalProfileLinkBuilder.IsAllowed(profileLink.Uri));
    }

    [Theory]
    [InlineData("BR1", "br")]
    [InlineData("EUN1", "eune")]
    [InlineData("EUW1", "euw")]
    [InlineData("JP1", "jp")]
    [InlineData("KR", "kr")]
    [InlineData("LA1", "lan")]
    [InlineData("LA2", "las")]
    [InlineData("NA1", "na")]
    [InlineData("OC1", "oce")]
    [InlineData("TR1", "tr")]
    public void ProfileLinkBuilderUsesNormalizedRegion(string region, string expectedRegion)
    {
        Assert.True(ExternalProfileLinkBuilder.TryBuild(ExternalProfileProvider.OpGg, "Player", "123", region, out ExternalProfileLink? profileLink));
        Assert.Equal($"/lol/summoners/{expectedRegion}/Player-123", profileLink.Uri.AbsolutePath);
    }

    [Fact]
    public void ProfileLinkBuilderEncodesRiotIdAsOnePathSegment()
    {
        Assert.True(ExternalProfileLinkBuilder.TryBuild(ExternalProfileProvider.OpGg, "A/B #雪", "T+1", "EUW", out ExternalProfileLink? profileLink));

        Assert.Equal("op.gg", profileLink.Uri.Host);
        Assert.EndsWith("/A%2FB%20%23%E9%9B%AA-T%2B1", profileLink.Uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.True(ExternalProfileLinkBuilder.IsAllowed(profileLink.Uri));
    }

    [Theory]
    [InlineData(null, "TAG", "EUW")]
    [InlineData("Player", null, "EUW")]
    [InlineData("Player", "TAG", "TEST9")]
    public void ProfileLinkBuilderRejectsIncompleteOrUnsupportedIdentity(
        string? gameName,
        string? tagLine,
        string region) =>
        Assert.False(ExternalProfileLinkBuilder.TryBuild(ExternalProfileProvider.OpGg, gameName, tagLine, region, out _));

    [Theory]
    [InlineData("http://op.gg/lol/summoners/euw/Player-Tag")]
    [InlineData("https://op.gg.evil.example/lol/summoners/euw/Player-Tag")]
    [InlineData("https://op.gg:8443/lol/summoners/euw/Player-Tag")]
    [InlineData("https://op.gg/lol/summoners/test/Player-Tag")]
    [InlineData("https://op.gg/lol/summoners/euw/Player-Tag?account=secret")]
    public void AllowlistRejectsUnsafeOrUnexpectedAddresses(string address) =>
        Assert.False(ExternalProfileLinkBuilder.IsAllowed(new(address)));

    [Fact]
    public void LauncherUsesTheWindowsDefaultBrowserForAllowedLinks()
    {
        ProcessStartInfo? capturedStartInfo = null;
        var launcher = new ExternalProfileLauncher(startInfo =>
        {
            capturedStartInfo = startInfo;
            return null;
        });
        Assert.True(ExternalProfileLinkBuilder.TryBuild(ExternalProfileProvider.DeepLol, "Player", "Tag", "EUW", out ExternalProfileLink? profileLink));

        ExternalProfileLaunchResult result = launcher.Open(profileLink.Uri);

        Assert.True(result.Succeeded);
        Assert.NotNull(capturedStartInfo);
        Assert.True(capturedStartInfo.UseShellExecute);
        Assert.Equal(profileLink.Uri.AbsoluteUri, capturedStartInfo.FileName);
    }

    [Fact]
    public void LauncherReturnsUserSafeErrorWhenWindowsCannotOpenBrowser()
    {
        var launcher = new ExternalProfileLauncher(_ => throw new Win32Exception("sensitive operating-system detail"));
        Assert.True(ExternalProfileLinkBuilder.TryBuild(ExternalProfileProvider.DpmLol, "Player", "Tag", "NA", out ExternalProfileLink? profileLink));

        ExternalProfileLaunchResult result = launcher.Open(profileLink.Uri);

        Assert.False(result.Succeeded);
        Assert.Equal("Windows could not open this profile in your default browser.", result.ErrorMessage);
    }

    [Fact]
    public void AccountDetailsReflectsExternalProfileSettingsImmediately()
    {
        var account = new VaultAccount
        {
            RiotGameName = "Player",
            RiotTagLine = "Tag",
            Region = "EUW"
        };
        using var viewModel = new AccountDetailsViewModel(account, new NullArtworkService(), new AppSettings());
        Assert.True(viewModel.ShowExternalProfileLinks);
        Assert.True(viewModel.CanOpenExternalProfileLinks);

        viewModel.UpdateSettings(new AppSettings
        {
            ShowExternalProfileLinks = false,
            ShowOpGgProfileLink = true,
            ShowDeepLolProfileLink = false,
            ShowDpmLolProfileLink = false,
            ShowLeagueOfGraphsProfileLink = false
        });

        Assert.False(viewModel.ShowExternalProfileLinks);
        Assert.False(viewModel.CanOpenExternalProfileLinks);
        Assert.True(viewModel.ShowOpGgProfileLink);
    }

    [Fact]
    public async Task SettingsRoundTripPreservesExternalProfileChoices()
    {
        string root = Path.Combine(Path.GetTempPath(), $"sv-profile-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var store = new AppSettingsStore(new(root));
            var expected = new AppSettings
            {
                ShowExternalProfileLinks = true,
                ShowOpGgProfileLink = false,
                ShowDeepLolProfileLink = true,
                ShowDpmLolProfileLink = false,
                ShowLeagueOfGraphsProfileLink = true
            };

            await store.SaveAsync(expected, TestContext.Current.CancellationToken);
            AppSettings actual = await store.LoadAsync(TestContext.Current.CancellationToken);

            Assert.True(actual.ShowExternalProfileLinks);
            Assert.False(actual.ShowOpGgProfileLink);
            Assert.True(actual.ShowDeepLolProfileLink);
            Assert.False(actual.ShowDpmLolProfileLink);
            Assert.True(actual.ShowLeagueOfGraphsProfileLink);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class NullArtworkService : IArtworkService
    {
        public Task<string?> ResolveAsync(string? assetPath, bool allowCommunityDragon, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public long GetCacheSizeBytes() => 0;
    }
}
