using System.Windows;
using SummonersVault.App.Services;
using SummonersVault.App.ViewModels;
using SummonersVault.Core.Abstractions;
using SummonersVault.Infrastructure.Backup;
using SummonersVault.Infrastructure.Artwork;
using SummonersVault.Infrastructure.League;
using SummonersVault.Infrastructure.Security;
using SummonersVault.Infrastructure.Settings;
using SummonersVault.Infrastructure.Storage;

namespace SummonersVault.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var paths = new VaultPaths();
        var repository = new EncryptedSqliteVaultRepository(paths);
        var session = new VaultSession(paths, repository);
        var league = new LeagueClientGateway();
        var settingsStore = new AppSettingsStore(paths);
        var backup = new VaultBackupService(paths, session);
        var clipboard = new SafeClipboardService(Dispatcher);
        var artwork = new ArtworkCacheService(paths, league);
        var viewModel = new MainViewModel(session, league, settingsStore, backup, clipboard, artwork);
        var window = new MainWindow(viewModel, artwork);
        MainWindow = window;
        window.Show();
        await viewModel.InitializeAsync();
    }
}
