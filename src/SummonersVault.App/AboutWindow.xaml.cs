using System.Windows;
using SummonersVault.App.Services;

namespace SummonersVault.App;

public partial class AboutWindow : Window
{
    internal AboutWindow(string version)
    {
        InitializeComponent();
        DarkTitleBar.Attach(this);
        VersionText.Text = $"Version {version}";
        Loaded += (_, _) => System.Windows.Input.Keyboard.Focus(CloseButton);
    }
}
