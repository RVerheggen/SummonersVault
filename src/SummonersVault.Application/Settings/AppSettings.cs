namespace SummonersVault.Application.Settings;

public sealed class AppSettings
{
    public int? AutoLockMinutes { get; set; } = 10;
    public bool LockOnSessionLockOrSleep { get; set; } = true;
    public string? LeagueInstallDirectory { get; set; }
    public bool DownloadCommunityDragonArtwork { get; set; } = true;
    public bool AutomaticallyCheckForUpdates { get; set; } = true;
    public DateTimeOffset? LastUpdateCheckAtUtc { get; set; }
}

public interface IAppSettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
