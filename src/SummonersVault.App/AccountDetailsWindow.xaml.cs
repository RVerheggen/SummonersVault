using System.ComponentModel;
using System.Windows;
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

    public AccountDetailsWindow(MainViewModel main, VaultAccount account, IArtworkService artwork)
    {
        InitializeComponent(); DarkTitleBar.Attach(this); _main = main; _artwork = artwork;
        DataContext = _details = new(account, artwork, main.Settings);
        _main.PropertyChanged += MainPropertyChanged;
    }

    private async void CopyUsername_Click(object sender, RoutedEventArgs e) => await _main.CopyLoginAsync(_details.Id);
    private async void CopyPassword_Click(object sender, RoutedEventArgs e) => await _main.CopyPasswordAsync(_details.Id);
    private async void OpenLeague_Click(object sender, RoutedEventArgs e) => await _main.LaunchAsync(_details.Id);
    private async void Sync_Click(object sender, RoutedEventArgs e)
    {
        string? error = await _main.SyncAsync(_details.Id);
        if (!string.IsNullOrWhiteSpace(error))
        {
            MessageBox.Show(this, error, "Unable to sync account", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        VaultAccount? account = await _main.GetAccountAsync(_details.Id); if (account is null) { Close(); return; }
        _details.Dispose(); DataContext = _details = new(account, _artwork, _main.Settings);
    }
    private void MainPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsVault) && !_main.IsVault)
        {
            Close();
        }
    }
    protected override void OnClosed(EventArgs e) { _main.PropertyChanged -= MainPropertyChanged; _details.Dispose(); base.OnClosed(e); }
}
