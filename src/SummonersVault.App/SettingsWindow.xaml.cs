using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SummonersVault.App.Services;
using SummonersVault.App.ViewModels;
using SummonersVault.Infrastructure.Settings;

namespace SummonersVault.App;

public partial class SettingsWindow : Window
{
    private readonly MainViewModel _viewModel;
    public SettingsWindow(MainViewModel viewModel)
    {
        InitializeComponent(); DarkTitleBar.Attach(this); _viewModel = viewModel;
        SessionLock.IsChecked = viewModel.Settings.LockOnSessionLockOrSleep;
        LeaguePath.Text = viewModel.Settings.LeagueInstallDirectory;
        CommunityDragonDownloads.IsChecked = viewModel.Settings.DownloadCommunityDragonArtwork;
        AutomaticUpdates.IsChecked = viewModel.Settings.AutomaticallyCheckForUpdates;
        RefreshUpdateDetails();
        UpdateCacheSize();
        foreach (ComboBoxItem item in AutoLock.Items)
            if ((viewModel.Settings.AutoLockMinutes is null && Equals(item.Tag, "never")) || Equals(item.Tag?.ToString(), viewModel.Settings.AutoLockMinutes?.ToString())) { AutoLock.SelectedItem = item; break; }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select Riot Games installation folder", Multiselect = false };
        if (dialog.ShowDialog(this) == true) LeaguePath.Text = dialog.FolderName;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var selection = (ComboBoxItem?)AutoLock.SelectedItem;
            int? minutes = Equals(selection?.Tag, "never") ? null : int.TryParse(selection?.Tag?.ToString(), out var value) ? value : 10;
            await _viewModel.SaveSettingsAsync(new AppSettings { AutoLockMinutes = minutes, LockOnSessionLockOrSleep = SessionLock.IsChecked == true, LeagueInstallDirectory = string.IsNullOrWhiteSpace(LeaguePath.Text) ? null : LeaguePath.Text.Trim(), DownloadCommunityDragonArtwork = CommunityDragonDownloads.IsChecked == true, AutomaticallyCheckForUpdates = AutomaticUpdates.IsChecked == true, LastUpdateCheckAtUtc = _viewModel.Settings.LastUpdateCheckAtUtc });
            if (CurrentPassword.SecurePassword.Length + NewPassword.SecurePassword.Length + ConfirmPassword.SecurePassword.Length > 0)
            {
                if (CurrentPassword.SecurePassword.Length == 0 || NewPassword.SecurePassword.Length == 0 || ConfirmPassword.SecurePassword.Length == 0) throw new ArgumentException("Complete all master-password fields.");
                await _viewModel.ChangeMasterPasswordAsync(SecurePasswordBytes.From(CurrentPassword.SecurePassword), SecurePasswordBytes.From(NewPassword.SecurePassword), SecurePasswordBytes.From(ConfirmPassword.SecurePassword));
            }
            DialogResult = true;
        }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException or InvalidDataException) { MessageBox.Show(this, ex.Message, "Settings", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async void ClearArtwork_Click(object sender, RoutedEventArgs e) { await _viewModel.Artwork.ClearAsync(); UpdateCacheSize(); }
    private void UpdateCacheSize() => CacheSize.Text = $"Artwork cache: {_viewModel.Artwork.GetCacheSizeBytes() / 1024d / 1024d:N1} MB of 256 MB";

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        try
        {
            var result = await _viewModel.CheckForUpdatesAsync(manual: true);
            RefreshUpdateDetails();
            if (result.Update is not null)
                new UpdateAvailableWindow(_viewModel, result.Update) { Owner = this }.ShowDialog();
            else if (result.State == UpdateCheckState.Failed)
                MessageBox.Show(this, result.Message, "Unable to check for updates", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { CheckUpdatesButton.IsEnabled = _viewModel.UpdatesAvailable; }
    }

    private void RefreshUpdateDetails()
    {
        VersionText.Text = $"Version {_viewModel.CurrentVersion}";
        LastUpdateCheck.Text = $"Last checked: {_viewModel.LastUpdateCheck}";
        UpdateStatus.Text = _viewModel.UpdateStatus;
        CheckUpdatesButton.IsEnabled = _viewModel.UpdatesAvailable;
    }
}
