using SummonersVault.App.Services;
using SummonersVault.Infrastructure.Settings;
using SummonersVault.Infrastructure.Storage;
using Xunit;

namespace SummonersVault.Tests;

public sealed class UpdateTests
{
    [Fact]
    public void ExistingSettingsDefaultToAutomaticChecksWithoutTimestamp()
    {
        var settings = new AppSettings();
        Assert.True(settings.AutomaticallyCheckForUpdates);
        Assert.Null(settings.LastUpdateCheckAtUtc);
    }

    [Fact]
    public async Task OlderSettingsJsonLoadsNewUpdateDefaults()
    {
        var root = Path.Combine(Path.GetTempPath(), $"sv-update-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var paths = new VaultPaths(root);
            await File.WriteAllTextAsync(paths.SettingsPath, "{\"AutoLockMinutes\":30,\"LockOnSessionLockOrSleep\":true}");

            var settings = await new AppSettingsStore(paths).LoadAsync();

            Assert.Equal(30, settings.AutoLockMinutes);
            Assert.True(settings.AutomaticallyCheckForUpdates);
            Assert.Null(settings.LastUpdateCheckAtUtc);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void AutomaticCheckRequiresPackagedBuildAndTwentyFourHours()
    {
        var service = new FakeUpdateService { IsPackaged = true };
        var workflow = new UpdateWorkflow(service);
        var now = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        var settings = new AppSettings { LastUpdateCheckAtUtc = now.AddHours(-23) };

        Assert.False(workflow.ShouldRunAutomaticCheck(settings, now));
        settings.LastUpdateCheckAtUtc = now.AddHours(-24);
        Assert.True(workflow.ShouldRunAutomaticCheck(settings, now));
        settings.AutomaticallyCheckForUpdates = false;
        Assert.False(workflow.ShouldRunAutomaticCheck(settings, now));
        settings.AutomaticallyCheckForUpdates = true;
        service.IsPackaged = false;
        Assert.False(workflow.ShouldRunAutomaticCheck(settings, now));
    }

    [Fact]
    public async Task ManualCheckBypassesScheduleAndSuccessfulCheckPersistsTimestamp()
    {
        var now = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        var service = new FakeUpdateService
        {
            IsPackaged = true,
            Result = new(UpdateCheckState.UpToDate, "Up to date")
        };
        var workflow = new UpdateWorkflow(service);
        var settings = new AppSettings { AutomaticallyCheckForUpdates = false };
        var saves = 0;

        var result = await workflow.CheckAsync(settings, manual: true, now, _ => { saves++; return Task.CompletedTask; });

        Assert.Equal(UpdateCheckState.UpToDate, result.State);
        Assert.Equal(now, settings.LastUpdateCheckAtUtc);
        Assert.Equal(1, saves);
        Assert.Equal(1, service.CheckCount);
    }

    [Fact]
    public async Task FailedCheckDoesNotAdvanceTimestamp()
    {
        var previous = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        var service = new FakeUpdateService
        {
            IsPackaged = true,
            Result = new(UpdateCheckState.Failed, "Offline")
        };
        var workflow = new UpdateWorkflow(service);
        var settings = new AppSettings { LastUpdateCheckAtUtc = previous };

        var result = await workflow.CheckAsync(settings, manual: true, previous.AddDays(2), _ => throw new InvalidOperationException("Should not save"));

        Assert.Equal(UpdateCheckState.Failed, result.State);
        Assert.Equal(previous, settings.LastUpdateCheckAtUtc);
    }

    [Fact]
    public async Task AutomaticCheckThatIsNotDueMakesNoNetworkCall()
    {
        var now = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        var service = new FakeUpdateService { IsPackaged = true };
        var workflow = new UpdateWorkflow(service);
        var settings = new AppSettings { LastUpdateCheckAtUtc = now.AddMinutes(-10) };

        var result = await workflow.CheckAsync(settings, manual: false, now, _ => Task.CompletedTask);

        Assert.Equal(UpdateCheckState.Unavailable, result.State);
        Assert.Equal(0, service.CheckCount);
    }

    [Fact]
    public async Task AvailableUpdateCountsAsSuccessfulCheck()
    {
        var now = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        var available = new AvailableUpdate("0.2.0", "Notes", new object());
        var service = new FakeUpdateService
        {
            IsPackaged = true,
            Result = new(UpdateCheckState.Available, "Available", available)
        };
        var settings = new AppSettings();

        var result = await new UpdateWorkflow(service).CheckAsync(settings, manual: false, now, _ => Task.CompletedTask);

        Assert.Same(available, result.Update);
        Assert.Equal(now, settings.LastUpdateCheckAtUtc);
    }

    [Theory]
    [InlineData("1.2.3", "0.1.0+abc", "9.9.9.9", "1.2.3")]
    [InlineData(null, "0.1.0+abc", "9.9.9.9", "0.1.0")]
    [InlineData(null, null, "9.9.9.9", "9.9.9")]
    public void VersionResolutionPrefersPackageThenInformationalThenAssembly(string? package, string? informational, string assembly, string expected)
    {
        Assert.Equal(expected, VelopackUpdateService.ResolveCurrentVersion(package, informational, Version.Parse(assembly)));
    }

    private sealed class FakeUpdateService : IUpdateService
    {
        public string CurrentVersion => "0.1.0";
        public bool IsPackaged { get; set; }
        public bool IsPortable => false;
        public int CheckCount { get; private set; }
        public UpdateCheckResult Result { get; set; } = new(UpdateCheckState.UpToDate, "Up to date");
        public Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default) { CheckCount++; return Task.FromResult(Result); }
        public Task<UpdateDownloadResult> DownloadAsync(AvailableUpdate update, IProgress<int> progress, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void ApplyAndRestart(AvailableUpdate update) => throw new NotSupportedException();
    }
}
