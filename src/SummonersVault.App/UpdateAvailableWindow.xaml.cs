using System.ComponentModel;
using System.Windows;
using SummonersVault.App.Services;
using SummonersVault.App.ViewModels;

namespace SummonersVault.App;

public partial class UpdateAvailableWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly AvailableUpdate _update;
    private CancellationTokenSource? _downloadCancellation;

    internal UpdateAvailableWindow(MainViewModel viewModel, AvailableUpdate update)
    {
        InitializeComponent();
        DarkTitleBar.Attach(this);
        _viewModel = viewModel;
        _update = update;
        VersionText.Text = $"Version {viewModel.CurrentVersion} to {update.Version}";
        ReleaseNotes.Text = update.ReleaseNotes;
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        DownloadButton.IsEnabled = false;
        LaterButton.Content = "Cancel download";
        DownloadProgress.Visibility = Visibility.Visible;
        DownloadProgress.Value = 0;
        StatusText.Text = "Downloading update...";
        _downloadCancellation = new CancellationTokenSource();

        try
        {
            var progress = new Progress<int>(value => DownloadProgress.Value = value);
            UpdateDownloadResult result = await _viewModel.DownloadUpdateAsync(_update, progress, _downloadCancellation.Token);
            StatusText.Text = result.Message;
            if (!result.Succeeded)
            {
                ResetDownloadControls();
                return;
            }

            DownloadProgress.Value = 100;
            StatusText.Text = "Securing the vault before restart...";
            await _viewModel.PrepareForUpdateAsync();
            _viewModel.ApplyUpdateAndRestart(_update);
        }
        catch (InvalidOperationException ex)
        {
            StatusText.Text = ex.Message;
            ResetDownloadControls();
        }
        catch (Exception)
        {
            StatusText.Text = "The update could not be started. Nothing was installed.";
            ResetDownloadControls();
        }
        finally
        {
            _downloadCancellation?.Dispose();
            _downloadCancellation = null;
        }
    }

    private void Later_Click(object sender, RoutedEventArgs e)
    {
        if (_downloadCancellation is not null)
        {
            _downloadCancellation.Cancel();
            LaterButton.IsEnabled = false;
            StatusText.Text = "Cancelling download...";
            return;
        }

        Close();
    }

    private void ResetDownloadControls()
    {
        DownloadButton.IsEnabled = true;
        LaterButton.IsEnabled = true;
        LaterButton.Content = "Later";
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_downloadCancellation is not null)
        {
            _downloadCancellation.Cancel();
            e.Cancel = true;
        }
        base.OnClosing(e);
    }
}
