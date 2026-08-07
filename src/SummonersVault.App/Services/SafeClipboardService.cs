using System.Windows;
using System.Windows.Threading;

namespace SummonersVault.App.Services;

public sealed class SafeClipboardService(Dispatcher dispatcher)
{
    private string? _ownedValue;
    private DispatcherTimer? _timer;

    public void Copy(string value)
    {
        Clipboard.SetText(value);
        _ownedValue = value;
        _timer?.Stop();
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Normal, (_, _) => ClearOwned(), dispatcher);
        _timer.Start();
    }

    public void ClearOwned()
    {
        _timer?.Stop();
        _timer = null;
        try
        {
            if (_ownedValue is not null && Clipboard.ContainsText() && string.Equals(Clipboard.GetText(), _ownedValue, StringComparison.Ordinal)) Clipboard.Clear();
        }
        catch (System.Runtime.InteropServices.COMException) { }
        _ownedValue = null;
    }
}

