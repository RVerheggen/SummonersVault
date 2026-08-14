using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SummonersVault.App.Services;
using SummonersVault.App.ViewModels;
using SummonersVault.Application.Abstractions;
using SummonersVault.Application.Accounts;
using SummonersVault.Application.League;
using SummonersVault.Application.Vault;
using SummonersVault.Application.Settings;
using SummonersVault.Application.Backup;
using SummonersVault.Application.ExternalProfiles;
using SummonersVault.Infrastructure.Backup;
using SummonersVault.Infrastructure.Artwork;
using SummonersVault.Infrastructure.League;
using SummonersVault.Infrastructure.Security;
using SummonersVault.Infrastructure.Settings;
using SummonersVault.Infrastructure.Storage;
using SummonersVault.Infrastructure.Persistence;
using Velopack;

namespace SummonersVault.App;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;
    [STAThread]
    private static void Main()
    {
        VelopackApp.Build().Run();
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _serviceProvider = ConfigureServices().BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        MainViewModel viewModel = _serviceProvider.GetRequiredService<MainViewModel>();
        IArtworkService artwork = _serviceProvider.GetRequiredService<IArtworkService>();
        IExternalProfileLauncher externalProfileLauncher = _serviceProvider.GetRequiredService<IExternalProfileLauncher>();
        var window = new MainWindow(viewModel, artwork, externalProfileLauncher);
        MainWindow = window;
        window.Show();
        await viewModel.InitializeAsync();
        window.FocusInitialPasswordInput();
        window.BeginAutomaticUpdateCheck();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.OnExit(e);
    }

    private ServiceCollection ConfigureServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<VaultPaths>();
        services.AddSingleton<EncryptedVaultStore>();
        services.AddSingleton<IVaultStore>(provider => provider.GetRequiredService<EncryptedVaultStore>());
        services.AddSingleton<IAccountRepository>(provider => provider.GetRequiredService<EncryptedVaultStore>());
        services.AddSingleton<VaultSession>();
        services.AddSingleton<IVaultSession>(provider => provider.GetRequiredService<VaultSession>());
        services.AddSingleton<IVaultFileAccess>(provider => provider.GetRequiredService<VaultSession>());
        services.AddSingleton<VaultService>();
        services.AddSingleton<AccountService>();
        services.AddSingleton<LeagueSynchronizationService>();
        services.AddSingleton<LeagueClientConnection>();
        services.AddSingleton<LeagueClientGateway>();
        services.AddSingleton<ILeagueClientGateway>(provider => provider.GetRequiredService<LeagueClientGateway>());
        services.AddSingleton<ILeagueClientConfiguration>(provider => provider.GetRequiredService<LeagueClientGateway>());
        services.AddSingleton<AppSettingsStore>();
        services.AddSingleton<IAppSettingsStore>(provider => provider.GetRequiredService<AppSettingsStore>());
        services.AddSingleton<SettingsService>();
        services.AddSingleton<VaultBackupService>();
        services.AddSingleton<IBackupService>(provider => provider.GetRequiredService<VaultBackupService>());
        services.AddSingleton<BackupService>();
        services.AddSingleton<SafeClipboardService>(_ => new SafeClipboardService(Dispatcher));
        services.AddSingleton<ArtworkCacheService>();
        services.AddSingleton<IArtworkService>(provider => provider.GetRequiredService<ArtworkCacheService>());
        services.AddSingleton<IExternalProfileLauncher, ExternalProfileLauncher>();
        services.AddSingleton<IUpdateService, VelopackUpdateService>();
        services.AddSingleton(provider => new MainViewModel(
            provider.GetRequiredService<VaultService>(),
            provider.GetRequiredService<AccountService>(),
            provider.GetRequiredService<LeagueSynchronizationService>(),
            provider.GetRequiredService<ILeagueClientGateway>(),
            provider.GetRequiredService<ILeagueClientConfiguration>(),
            provider.GetRequiredService<SettingsService>(),
            provider.GetRequiredService<BackupService>(),
            provider.GetRequiredService<SafeClipboardService>(),
            provider.GetRequiredService<IArtworkService>(),
            provider.GetRequiredService<IUpdateService>()));
        return services;
    }
}
