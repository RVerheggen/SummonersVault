using System.Text.Json;
using System.Text.Json.Serialization;
using SummonersVault.Application.Settings;
using SummonersVault.Infrastructure.Storage;

namespace SummonersVault.Infrastructure.Settings;

public sealed class AppSettingsStore(VaultPaths paths) : IAppSettingsStore
{
    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(paths.SettingsPath))
        {
            return new();
        }

        try
        {
            await using FileStream stream = File.OpenRead(paths.SettingsPath);
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
