namespace SummonersVault.Application.Settings;

public sealed class SettingsService(IAppSettingsStore store)
{
    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) =>
        store.LoadAsync(cancellationToken);

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) =>
        store.SaveAsync(settings, cancellationToken);
}
