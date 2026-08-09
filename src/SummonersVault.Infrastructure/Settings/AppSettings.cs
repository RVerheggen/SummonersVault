using System.Text.Json;
using System.Text.Json.Serialization;
using SummonersVault.Infrastructure.Storage;

namespace SummonersVault.Infrastructure.Settings;

public sealed class AppSettings
{
    public int? AutoLockMinutes { get; set; } = 10;
    public bool LockOnSessionLockOrSleep { get; set; } = true;
    public string? LeagueInstallDirectory { get; set; }
    public bool DownloadCommunityDragonArtwork { get; set; } = true;
}

public sealed class AppSettingsStore(VaultPaths paths)
{
    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(paths.SettingsPath)) return new();
        try
        {
            await using var stream = File.OpenRead(paths.SettingsPath);
            return await JsonSerializer.DeserializeAsync(stream, AppSettingsJsonContext.Default.AppSettings, cancellationToken).ConfigureAwait(false) ?? new();
        }
        catch (JsonException) { return new(); }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        paths.EnsureCreated();
        await using var stream = new FileStream(paths.SettingsPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, settings, AppSettingsJsonContext.Default.AppSettings, cancellationToken).ConfigureAwait(false);
    }
}

[JsonSerializable(typeof(AppSettings))]
internal sealed partial class AppSettingsJsonContext : JsonSerializerContext;
