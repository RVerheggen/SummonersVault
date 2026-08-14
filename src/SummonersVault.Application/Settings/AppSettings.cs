namespace SummonersVault.Application.Settings;

using SummonersVault.Application.ExternalProfiles;
using System.Text.Json.Serialization;

public sealed class AppSettings
{
    public int? AutoLockMinutes { get; set; } = 10;
    public bool LockOnSessionLockOrSleep { get; set; } = true;
    public string? LeagueInstallDirectory { get; set; }
    public bool DownloadCommunityDragonArtwork { get; set; } = true;
    public bool ShowExternalProfileLinks { get; set; } = true;
    public bool ShowOpGgProfileLink { get; set; } = true;
    public bool ShowDeepLolProfileLink { get; set; } = true;
    public bool ShowDpmLolProfileLink { get; set; } = true;
    public bool ShowLeagueOfGraphsProfileLink { get; set; } = true;
    public bool AutomaticallyCheckForUpdates { get; set; } = true;
    public DateTimeOffset? LastUpdateCheckAtUtc { get; set; }

    [JsonIgnore]
    public bool HasEnabledExternalProfileProvider => ShowOpGgProfileLink
        || ShowDeepLolProfileLink
        || ShowDpmLolProfileLink
        || ShowLeagueOfGraphsProfileLink;

    public bool IsExternalProfileProviderEnabled(ExternalProfileProvider provider) => provider switch
    {
        ExternalProfileProvider.OpGg => ShowOpGgProfileLink,
        ExternalProfileProvider.DeepLol => ShowDeepLolProfileLink,
        ExternalProfileProvider.DpmLol => ShowDpmLolProfileLink,
        ExternalProfileProvider.LeagueOfGraphs => ShowLeagueOfGraphsProfileLink,
        _ => false
    };
}

public interface IAppSettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
