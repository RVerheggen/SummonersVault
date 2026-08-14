using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using SummonersVault.App.Services;
using SummonersVault.App.ViewModels;
using SummonersVault.Application.Abstractions;
using SummonersVault.Application.ExternalProfiles;
using SummonersVault.Core.Models;

namespace SummonersVault.App;

public partial class AccountDetailsWindow : Window
{
    private readonly MainViewModel _main;
    private readonly IArtworkService _artwork;
    private readonly IExternalProfileLauncher _externalProfileLauncher;
    private AccountDetailsViewModel _details;
    private readonly DispatcherTimer _galleryResizeTimer;

    public AccountDetailsWindow(
        MainViewModel main,
        VaultAccount account,
        IArtworkService artwork,
        IExternalProfileLauncher externalProfileLauncher)
    {
        InitializeComponent(); DarkTitleBar.Attach(this); _main = main; _artwork = artwork; _externalProfileLauncher = externalProfileLauncher;
        _galleryResizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _galleryResizeTimer.Tick += GalleryResizeTimer_Tick;
        SizeChanged += AccountDetailsWindow_SizeChanged;
        DataContext = _details = new(account, artwork, main.Settings);
        Loaded += (_, _) => _details.UpdateGalleryViewport(ActualWidth - 80);
        _main.PropertyChanged += MainPropertyChanged;
        _main.ChampionProgressionUpdated += ChampionProgressionUpdated;
        _main.ChampionProgressionSynchronizationFinished += ChampionProgressionSynchronizationFinished;
    }
    private void ChampionCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ChampionGalleryItem champion })
        {
            _details.SelectChampion(champion);
        }
    }

    private void ChampionBack_Click(object sender, RoutedEventArgs e) => _details.ShowChampionGallery();

    private void AccountDetailsWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _galleryResizeTimer.Stop();
        _galleryResizeTimer.Start();
    }

    private void GalleryResizeTimer_Tick(object? sender, EventArgs e)
    {
        _galleryResizeTimer.Stop();
        _details.UpdateGalleryViewport(ActualWidth - 80);
    }

    private async void CopyUsername_Click(object sender, RoutedEventArgs e) => await _main.CopyLoginAsync(_details.Id);
    private async void CopyPassword_Click(object sender, RoutedEventArgs e) => await _main.CopyPasswordAsync(_details.Id);
    private async void OpenLeague_Click(object sender, RoutedEventArgs e) => await _main.LaunchAsync(_details.Id);
    private void ViewStats_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button)
        {
            return;
        }

        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = !menu.IsOpen;
    }

    private void OpenExternalProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: ExternalProfileProvider provider }
            || !_main.Settings.ShowExternalProfileLinks
            || !_main.Settings.IsExternalProfileProviderEnabled(provider))
        {
            return;
        }

        VaultAccount account = _details.Account;
        if (!ExternalProfileLinkBuilder.TryBuild(
            provider,
            account.RiotGameName,
            account.RiotTagLine,
            account.Region,
            out ExternalProfileLink? profileLink))
        {
            MessageBox.Show(this, "Sync this account before opening an external profile.", "Profile unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ExternalProfileLaunchResult result = _externalProfileLauncher.Open(profileLink.Uri);
        if (!result.Succeeded)
        {
            MessageBox.Show(this, result.ErrorMessage, $"Unable to open {profileLink.ProviderName}", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Sync_Click(object sender, RoutedEventArgs e)
    {
        _details.SynchronizationStatus = "Synchronizing League account...";
        string? error = await _main.SyncAsync(_details.Id);
        if (!string.IsNullOrWhiteSpace(error))
        {
            _details.SynchronizationStatus = string.Empty;
            MessageBox.Show(this, error, "Unable to sync account", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            _details.SynchronizationStatus = _main.IsChampionProgressionSynchronizing(_details.Id)
                ? "Synchronizing champion mastery and Eternals..."
                : string.Empty;
        }

        await ReloadAsync();
    }
    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        VaultAccount? account = await _main.GetAccountAsync(_details.Id); if (account is null)
        {
            return;
        }

        var dialog = new AccountDialog(account) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (dialog.DeleteRequested) { await _main.DeleteAsync(account.Id); Close(); return; }
        if (dialog.Result is not null)
        {
            await _main.SaveAccountAsync(dialog.Result);
        }

        await ReloadAsync();
    }
    private async Task ReloadAsync()
    {
        Guid accountId = _details.Id;
        int? selectedChampionId = _details.SelectedChampion?.Champion.ChampionId;
        string championQuery = _details.ChampionQuery;
        string championCollection = _details.ChampionCollection;
        string championSort = _details.ChampionSort;
        string championSortDirection = _details.ChampionSortDirection;
        int galleryColumnCount = _details.GalleryColumnCount;
        int craftingColumnCount = _details.CraftingColumnCount;
        VaultAccount? account = await _main.GetAccountAsync(accountId);
        if (account is null)
        {
            Close();
            return;
        }

        var updated = new AccountDetailsViewModel(account, _artwork, _main.Settings)
        {
            ChampionQuery = championQuery,
            ChampionCollection = championCollection,
            ChampionSort = championSort,
            ChampionSortDirection = championSortDirection,
            GalleryColumnCount = galleryColumnCount,
            CraftingColumnCount = craftingColumnCount,
            SynchronizationStatus = _main.IsChampionProgressionSynchronizing(accountId)
                ? "Synchronizing champion mastery and Eternals..."
                : string.Empty
        };
        if (selectedChampionId is { } championId
            && updated.Champions.FirstOrDefault(champion => champion.Champion.ChampionId == championId) is { } selectedChampion)
        {
            updated.SelectChampion(selectedChampion);
        }

        _details.Dispose();
        DataContext = _details = updated;
    }

    private async void ChampionProgressionUpdated(Guid accountId)
    {
        if (accountId == _details.Id && IsLoaded)
        {
            _details.SynchronizationStatus = string.Empty;
            await ReloadAsync();
        }
    }

    private void ChampionProgressionSynchronizationFinished(Guid accountId)
    {
        if (accountId == _details.Id)
        {
            _details.SynchronizationStatus = string.Empty;
        }
    }
    private void MainPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsVault) && !_main.IsVault)
        {
            Close();
        }
        else if (e.PropertyName == nameof(MainViewModel.Settings))
        {
            _details.UpdateSettings(_main.Settings);
        }
    }
    protected override void OnClosed(EventArgs e) { _galleryResizeTimer.Stop(); SizeChanged -= AccountDetailsWindow_SizeChanged; _main.PropertyChanged -= MainPropertyChanged; _main.ChampionProgressionUpdated -= ChampionProgressionUpdated; _main.ChampionProgressionSynchronizationFinished -= ChampionProgressionSynchronizationFinished; _details.Dispose(); base.OnClosed(e); }
}
