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
            await _viewModel.SaveSettingsAsync(new AppSettings { AutoLockMinutes = minutes, LockOnSessionLockOrSleep = SessionLock.IsChecked == true, LeagueInstallDirectory = string.IsNullOrWhiteSpace(LeaguePath.Text) ? null : LeaguePath.Text.Trim() });
            if (CurrentPassword.SecurePassword.Length + NewPassword.SecurePassword.Length + ConfirmPassword.SecurePassword.Length > 0)
            {
                if (CurrentPassword.SecurePassword.Length == 0 || NewPassword.SecurePassword.Length == 0 || ConfirmPassword.SecurePassword.Length == 0) throw new ArgumentException("Complete all master-password fields.");
                await _viewModel.ChangeMasterPasswordAsync(SecurePasswordBytes.From(CurrentPassword.SecurePassword), SecurePasswordBytes.From(NewPassword.SecurePassword), SecurePasswordBytes.From(ConfirmPassword.SecurePassword));
            }
            DialogResult = true;
        }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException or InvalidDataException) { MessageBox.Show(this, ex.Message, "Settings", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
