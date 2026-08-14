using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using SummonersVault.App.Services;
using SummonersVault.App.ViewModels;
using SummonersVault.Application.Abstractions;
using SummonersVault.Core.Models;

namespace SummonersVault.App;

public partial class AccountDetailsWindow : Window
{
    private readonly MainViewModel _main;
    private readonly IArtworkService _artwork;
    private AccountDetailsViewModel _details;
    private readonly DispatcherTimer _galleryResizeTimer;

    public AccountDetailsWindow(MainViewModel main, VaultAccount account, IArtworkService artwork)
    {
        InitializeComponent(); DarkTitleBar.Attach(this); _main = main; _artwork = artwork;
        _galleryResizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _galleryResizeTimer.Tick += GalleryResizeTimer_Tick;
        SizeChanged += AccountDetailsWindow_SizeChanged;
        DataContext = _details = new(account, artwork, main.Settings);
        Loaded += (_, _) => _details.UpdateChampionViewport(ActualWidth - 80);
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
        _details.UpdateChampionViewport(ActualWidth - 80);
    }

    private async void CopyUsername_Click(object sender, RoutedEventArgs e) => await _main.CopyLoginAsync(_details.Id);
    private async void CopyPassword_Click(object sender, RoutedEventArgs e) => await _main.CopyPasswordAsync(_details.Id);
    private async void OpenLeague_Click(object sender, RoutedEventArgs e) => await _main.LaunchAsync(_details.Id);
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
        int championColumnCount = _details.ChampionColumnCount;
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
            ChampionColumnCount = championColumnCount,
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
    }
    protected override void OnClosed(EventArgs e) { _galleryResizeTimer.Stop(); SizeChanged -= AccountDetailsWindow_SizeChanged; _main.PropertyChanged -= MainPropertyChanged; _main.ChampionProgressionUpdated -= ChampionProgressionUpdated; _main.ChampionProgressionSynchronizationFinished -= ChampionProgressionSynchronizationFinished; _details.Dispose(); base.OnClosed(e); }
}
