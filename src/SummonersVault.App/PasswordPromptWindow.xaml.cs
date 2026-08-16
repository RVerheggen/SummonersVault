using System.Security;
using System.Windows;
using SummonersVault.App.Services;
namespace SummonersVault.App;

public partial class PasswordPromptWindow : Window
{
    public PasswordPromptWindow()
    {
        InitializeComponent();
        DarkTitleBar.Attach(this);
        Loaded += (_, _) => System.Windows.Input.Keyboard.Focus(Password);
    }
    public SecureString SecurePassword => Password.SecurePassword;
    private void Continue_Click(object sender, RoutedEventArgs e) { if (Password.SecurePassword.Length == 0) { return; } DialogResult = true; }
}
