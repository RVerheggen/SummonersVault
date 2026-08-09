using System.Windows;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SummonersVault.Core.Abstractions;

namespace SummonersVault.App.Controls;

public sealed class ArtworkImage : Grid
{
    public static readonly DependencyProperty AssetPathProperty = DependencyProperty.Register(nameof(AssetPath), typeof(string), typeof(ArtworkImage), new(OnAssetChanged));
    public static readonly DependencyProperty ArtworkServiceProperty = DependencyProperty.Register(nameof(ArtworkService), typeof(IArtworkService), typeof(ArtworkImage), new(OnAssetChanged));
    public static readonly DependencyProperty AllowCommunityDragonProperty = DependencyProperty.Register(nameof(AllowCommunityDragon), typeof(bool), typeof(ArtworkImage), new(true, OnAssetChanged));
    private readonly Image _image = new() { Stretch = Stretch.UniformToFill };
    private CancellationTokenSource? _load;
    private Border? _clipHost;

    public ArtworkImage()
    {
        Background = (Brush)Application.Current.FindResource("SurfaceMutedBrush");
        Children.Add(new TextBlock { Text = "◇", FontSize = 28, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)Application.Current.FindResource("HighlightBrush") });
        Children.Add(_image);
        Loaded += (_, _) => { AttachHostClip(); StartLoad(); };
        Unloaded += (_, _) => { CancelLoad(); DetachHostClip(); };
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Clip = ActualWidth > 0 && ActualHeight > 0
            ? new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), 16, 16)
            : null;
    }

    private void AttachHostClip()
    {
        DependencyObject? current = this;
        while ((current = VisualTreeHelper.GetParent(current)) is not null && current is not Border) { }
        if (current is not Border border) return;
        _clipHost = border;
        _clipHost.SizeChanged += HostSizeChanged;
        UpdateHostClip();
    }

    private void HostSizeChanged(object sender, SizeChangedEventArgs e) => UpdateHostClip();

    private void UpdateHostClip()
    {
        if (_clipHost is not { ActualWidth: > 0, ActualHeight: > 0 } host) return;
        host.Clip = new RectangleGeometry(new Rect(0, 0, host.ActualWidth, host.ActualHeight), 16, 16);
    }

    private void DetachHostClip()
    {
        if (_clipHost is null) return;
        _clipHost.SizeChanged -= HostSizeChanged;
        _clipHost = null;
    }

    public string? AssetPath { get => (string?)GetValue(AssetPathProperty); set => SetValue(AssetPathProperty, value); }
    public IArtworkService? ArtworkService { get => (IArtworkService?)GetValue(ArtworkServiceProperty); set => SetValue(ArtworkServiceProperty, value); }
    public bool AllowCommunityDragon { get => (bool)GetValue(AllowCommunityDragonProperty); set => SetValue(AllowCommunityDragonProperty, value); }

    private static void OnAssetChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((ArtworkImage)sender).StartLoad();

    private async void StartLoad()
    {
        if (!IsLoaded || ArtworkService is null || string.IsNullOrWhiteSpace(AssetPath)) return;
        CancelLoad();
        _load = new CancellationTokenSource();
        try
        {
            var path = await ArtworkService.ResolveAsync(AssetPath, AllowCommunityDragon, _load.Token);
            if (path is null) return;
            var bitmap = new BitmapImage();
            bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.UriSource = new Uri(path); bitmap.EndInit(); bitmap.Freeze();
            _image.Source = bitmap;
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (NotSupportedException) { }
    }

    private void CancelLoad() { _load?.Cancel(); _load?.Dispose(); _load = null; }
}
