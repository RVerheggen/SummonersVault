using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using SummonersVault.App.Services;
using SummonersVault.App.ViewModels;
using SummonersVault.Application.Abstractions;
using SummonersVault.Application.ExternalProfiles;
using SummonersVault.Core.Models;

namespace SummonersVault.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IArtworkService _artwork;
    private readonly IExternalProfileLauncher _externalProfileLauncher;
    private readonly DispatcherTimer _lockTimer;
    private readonly DispatcherTimer _clientTimer;
    private DateTimeOffset _lastActivity = DateTimeOffset.UtcNow;

    public MainWindow(
        MainViewModel viewModel,
        IArtworkService artwork,
        IExternalProfileLauncher externalProfileLauncher)
    {
        InitializeComponent(); DarkTitleBar.Attach(this); DataContext = _viewModel = viewModel; _artwork = artwork; _externalProfileLauncher = externalProfileLauncher;
        PreviewMouseDown += (_, _) => MarkActive(); PreviewKeyDown += (_, _) => MarkActive(); PreviewTouchDown += (_, _) => MarkActive();
        _lockTimer = new DispatcherTimer(TimeSpan.FromSeconds(10), DispatcherPriority.Background, CheckAutoLock, Dispatcher);
        _clientTimer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, CheckClient, Dispatcher);
        SystemEvents.SessionSwitch += SessionSwitch; SystemEvents.PowerModeChanged += PowerModeChanged;
    }

    private async void CreateVault_Click(object sender, RoutedEventArgs e) => await RunPasswordActionAsync(() => _viewModel.CreateAsync(SecurePasswordBytes.From(CreatePassword.SecurePassword), SecurePasswordBytes.From(CreateConfirmation.SecurePassword)), CreatePassword, CreateConfirmation);
    private async void UnlockVault_Click(object sender, RoutedEventArgs e)
    {
        await RunPasswordActionAsync(async () =>
        {
            if (await _viewModel.UnlockAsync(SecurePasswordBytes.From(UnlockPassword.SecurePassword)))
            {
                MarkActive();
            }
            else
            {
                RestoreUnlockPasswordFocus();
            }
        }, UnlockPassword);
    }

    private void RestoreUnlockPasswordFocus()
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (_viewModel.IsLocked && UnlockPassword.IsEnabled)
            {
                UnlockPassword.Focus();
                Keyboard.Focus(UnlockPassword);
            }
        }, DispatcherPriority.Input);
    }

    internal void FocusInitialPasswordInput()
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            PasswordBox passwordBox = _viewModel.IsOnboarding ? CreatePassword : UnlockPassword;
            if (passwordBox.IsEnabled)
            {
                passwordBox.Focus();
                Keyboard.Focus(passwordBox);
            }
        }, DispatcherPriority.Input);
    }

    private void UnlockPassword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        if (!e.IsRepeat && _viewModel.CanAuthenticate)
        {
            UnlockVault_Click(sender, e);
        }
    }
    private async void Lock_Click(object sender, RoutedEventArgs e) => await _viewModel.LockAsync();

    private async Task RunPasswordActionAsync(Func<Task> action, params PasswordBox[] boxes)
    {
        if (!_viewModel.CanAuthenticate)
        {
            return;
        }

        try
        {
            await action(); foreach (PasswordBox box in boxes)
            {
                box.Clear();
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or InvalidDataException) { MessageBox.Show(this, ex.Message, "SummonersVault", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async void AddAccount_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AccountDialog(null) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            await _viewModel.SaveAccountAsync(dialog.Result);
        }
    }

    private async void EditAccount_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetId(sender, out Guid id))
        {
            return;
        }

        VaultAccount? account = await _viewModel.GetAccountAsync(id); if (account is null)
        {
            return;
        }

        new AccountDetailsWindow(_viewModel, account, _artwork, _externalProfileLauncher) { Owner = this }.Show();
    }

    private async void CopyLogin_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetId(sender, out Guid id))
        {
            await _viewModel.CopyLoginAsync(id);
        }
    }
    private async void CopyPassword_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetId(sender, out Guid id))
        {
            await _viewModel.CopyPasswordAsync(id);
        }
    }
    private async void Launch_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetId(sender, out Guid id))
        {
            await _viewModel.LaunchAsync(id);
        }
    }
    private async void Sync_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetId(sender, out Guid id))
        {
            return;
        }

        string? error = await _viewModel.SyncAsync(id);
        if (!string.IsNullOrWhiteSpace(error))
        {
            MessageBox.Show(this, error, "Unable to sync account", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
    private void ClearFilters_Click(object sender, RoutedEventArgs e) => _viewModel.ClearFilters();
    private void Sort_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _viewModel?.SortRecentlyPlayed = ((ComboBox)sender).SelectedIndex == 1;
    }
    private void Settings_Click(object sender, RoutedEventArgs e) => new SettingsWindow(_viewModel) { Owner = this }.ShowDialog();
    private void About_Click(object sender, RoutedEventArgs e) => MessageBox.Show(this, $"SummonersVault {_viewModel.CurrentVersion}\n\nSummonersVault is a free, local-only community password manager and League companion.\n\nSummonersVault isn't endorsed by Riot Games and doesn't reflect the views or opinions of Riot Games or anyone officially involved in producing or managing Riot Games properties. Riot Games and all associated properties are trademarks or registered trademarks of Riot Games, Inc.\n\nNo credentials are sent to Riot by this app.", "About SummonersVault", MessageBoxButton.OK, MessageBoxImage.Information);

    internal async void BeginAutomaticUpdateCheck()
    {
        if (!_viewModel.ShouldRunAutomaticUpdateCheck(DateTimeOffset.UtcNow))
        {
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(2));
        if (!IsVisible)
        {
            return;
        }

        UpdateCheckResult result = await _viewModel.CheckForUpdatesAsync(manual: false);
        if (result.Update is not null)
        {
            new UpdateAvailableWindow(_viewModel, result.Update) { Owner = this }.ShowDialog();
        }
    }

    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        MessageBoxResult direction = MessageBox.Show(this, "Choose Yes to export a backup, or No to import one.", "Import / Export", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (direction == MessageBoxResult.Cancel)
        {
            return;
        }

        try
        {
            if (direction == MessageBoxResult.Yes)
            {
                var save = new SaveFileDialog { Filter = "SummonersVault backup (*.svault)|*.svault", AddExtension = true, DefaultExt = ".svault", FileName = $"SummonersVault-{DateTime.Now:yyyy-MM-dd}.svault" };
                if (save.ShowDialog(this) != true)
                {
                    return;
                }

                await _viewModel.Backup.ExportAsync(save.FileName); MessageBox.Show(this, "Encrypted backup exported.", "Backup", MessageBoxButton.OK, MessageBoxImage.Information); return;
            }
            var open = new OpenFileDialog { Filter = "SummonersVault backup (*.svault)|*.svault", CheckFileExists = true };
            if (open.ShowDialog(this) != true)
            {
                return;
            }

            var prompt = new PasswordPromptWindow { Owner = this }; if (prompt.ShowDialog() != true)
            {
                return;
            }

            byte[] password = SecurePasswordBytes.From(prompt.SecurePassword);
            BackupImportPreview preview;
            try { preview = await _viewModel.Backup.PreviewImportAsync(open.FileName, password); }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(password); }
            await using (preview)
            {
                var choices = new Dictionary<Guid, BackupConflictChoice>();
                foreach (BackupConflict conflict in preview.Conflicts)
                {
                    MessageBoxResult choice = MessageBox.Show(this, $"{conflict.DisplayName} already exists.\n\nYes: use imported\nNo: keep current", "Import conflict", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
                    choices[conflict.ImportedId] = choice == MessageBoxResult.Yes ? BackupConflictChoice.UseImported : BackupConflictChoice.KeepCurrent;
                }
                await _viewModel.Backup.ImportAsync(preview, choices); await _viewModel.RefreshAsync(); MessageBox.Show(this, "Backup merged into this vault.", "Import", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or UnauthorizedAccessException or IOException or ArgumentException) { MessageBox.Show(this, ex.Message, "Import / Export", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void MarkActive() => _lastActivity = DateTimeOffset.UtcNow;
    private async void CheckAutoLock(object? sender, EventArgs e)
    {
        if (_viewModel.IsVault && _viewModel.Settings.AutoLockMinutes is { } minutes && DateTimeOffset.UtcNow - _lastActivity >= TimeSpan.FromMinutes(minutes))
        {
            await _viewModel.LockAsync();
        }
    }
    private async void CheckClient(object? sender, EventArgs e)
    {
        if (_viewModel.IsVault)
        {
            await _viewModel.UpdateClientStatusAsync();
        }
    }
    private async void SessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock && _viewModel.IsVault && _viewModel.Settings.LockOnSessionLockOrSleep)
        {
            await Dispatcher.InvokeAsync(_viewModel.LockAsync);
        }
    }
    private async void PowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend && _viewModel.IsVault && _viewModel.Settings.LockOnSessionLockOrSleep)
        {
            await Dispatcher.InvokeAsync(_viewModel.LockAsync);
        }
    }
    private static bool TryGetId(object sender, out Guid id) => Guid.TryParse((sender as FrameworkElement)?.Tag?.ToString(), out id);

    protected override async void OnClosing(CancelEventArgs e)
    {
        _lockTimer.Stop(); _clientTimer.Stop(); SystemEvents.SessionSwitch -= SessionSwitch; SystemEvents.PowerModeChanged -= PowerModeChanged; await _viewModel.DisposeAsync(); base.OnClosing(e);
    }
}
