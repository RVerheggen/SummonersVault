using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using SummonersVault.App.Services;
using SummonersVault.App.Themes;
using SummonersVault.App.ViewModels;
using SummonersVault.Application.Abstractions;

namespace SummonersVault.App;

public partial class BackupWindow : Window
{
    private const string BackupFileFilter = "SummonersVault backup (*.svault)|*.svault";
    private readonly MainViewModel _viewModel;
    private CancellationTokenSource? _operationCancellation;
    private bool _closeWhenIdle;

    internal BackupWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DarkTitleBar.Attach(this);
        _viewModel = viewModel;
        Loaded += (_, _) => System.Windows.Input.Keyboard.Focus(ExportButton);
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = BackupFileFilter,
            AddExtension = true,
            DefaultExt = ".svault",
            FileName = $"SummonersVault-{DateTime.Now:yyyy-MM-dd}.svault"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await RunOperationAsync(
            "Exporting encrypted backup...",
            async cancellationToken =>
            {
                await _viewModel.Backup.ExportAsync(dialog.FileName, cancellationToken);
                return true;
            },
            "Encrypted backup exported successfully.");
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = BackupFileFilter,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var passwordPrompt = new PasswordPromptWindow { Owner = this };
        if (passwordPrompt.ShowDialog() != true)
        {
            return;
        }

        byte[] password = SecurePasswordBytes.From(passwordPrompt.SecurePassword);
        await RunOperationAsync(
            "Validating encrypted backup...",
            async cancellationToken =>
            {
                BackupImportPreview preview;
                try
                {
                    preview = await _viewModel.Backup.PreviewImportAsync(dialog.FileName, password, cancellationToken);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(password);
                }

                await using (preview)
                {
                    var choices = new Dictionary<Guid, BackupConflictChoice>();
                    foreach (BackupConflict conflict in preview.Conflicts)
                    {
                        var conflictWindow = new BackupConflictWindow(conflict) { Owner = this };
                        if (conflictWindow.ShowDialog() != true || conflictWindow.Choice is not { } choice)
                        {
                            return false;
                        }

                        choices[conflict.ImportedId] = choice;
                    }

                    ShowStatus("Merging backup into this vault...", OperationStatus.Working, showProgress: true);
                    await _viewModel.Backup.ImportAsync(preview, choices, cancellationToken);
                    await _viewModel.RefreshAsync();
                    return true;
                }
            },
            "Backup merged into this vault.");
    }

    private async Task RunOperationAsync(
        string workingMessage,
        Func<CancellationToken, Task<bool>> operation,
        string successMessage)
    {
        SetBusy(true);
        ShowStatus(workingMessage, OperationStatus.Working, showProgress: true);
        _operationCancellation = new CancellationTokenSource();
        try
        {
            bool completed = await operation(_operationCancellation.Token);
            ShowStatus(
                completed ? successMessage : "Import cancelled. No changes were made.",
                completed ? OperationStatus.Success : OperationStatus.Neutral);
        }
        catch (OperationCanceledException) when (_operationCancellation?.IsCancellationRequested == true)
        {
            ShowStatus("Operation cancelled. No changes were made.", OperationStatus.Neutral);
        }
        catch (Exception exception) when (exception is InvalidDataException
            or UnauthorizedAccessException
            or IOException
            or ArgumentException
            or InvalidOperationException)
        {
            ShowStatus(exception.Message, OperationStatus.Error);
        }
        catch (Exception)
        {
            ShowStatus("The operation could not be completed. No changes were made.", OperationStatus.Error);
        }
        finally
        {
            _operationCancellation.Dispose();
            _operationCancellation = null;
            SetBusy(false);
            if (_closeWhenIdle)
            {
                Close();
            }
        }
    }

    private void SetBusy(bool isBusy)
    {
        ExportButton.IsEnabled = !isBusy;
        ImportButton.IsEnabled = !isBusy;
        CloseButton.IsEnabled = !isBusy;
    }

    private void ShowStatus(string message, OperationStatus status, bool showProgress = false)
    {
        string brushResource = status switch
        {
            OperationStatus.Success => ThemeResourceKeys.SuccessBrush,
            OperationStatus.Error => ThemeResourceKeys.DangerBrush,
            _ => ThemeResourceKeys.HighlightBrush
        };
        StatusSymbol.Text = status switch
        {
            OperationStatus.Success => "✓",
            OperationStatus.Error => "!",
            OperationStatus.Working => "◇",
            _ => "•"
        };
        StatusSymbol.Foreground = (Brush)FindResource(brushResource);
        StatusText.Text = message;
        OperationProgress.Visibility = showProgress ? Visibility.Visible : Visibility.Collapsed;
        StatusPanel.Visibility = Visibility.Visible;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_operationCancellation is not null)
        {
            _closeWhenIdle = true;
            _operationCancellation.Cancel();
            e.Cancel = true;
        }

        base.OnClosing(e);
    }

    private enum OperationStatus
    {
        Neutral,
        Working,
        Success,
        Error
    }
}
