using System.Windows;
using SummonersVault.App.Services;
using SummonersVault.Application.Accounts;
using SummonersVault.Application.Security;
using SummonersVault.Core.Models;

namespace SummonersVault.App;

public partial class AccountDialog : Window
{
    private readonly VaultAccount? _existingAccount;

    public AccountDialog(VaultAccount? account)
    {
        InitializeComponent();
        DarkTitleBar.Attach(this);
        _existingAccount = account;

        string normalizedRegion = LeagueRegion.Normalize(account?.Region ?? LeagueRegion.EuropeWest);
        var regions = new List<string>(LeagueRegion.Supported);
        if (!regions.Contains(normalizedRegion, StringComparer.OrdinalIgnoreCase))
        {
            regions.Add(normalizedRegion);
        }

        RegionText.ItemsSource = regions;
        RegionText.SelectedItem = normalizedRegion;
        DeleteButton.Visibility = account is null ? Visibility.Collapsed : Visibility.Visible;
        if (account is null)
        {
            return;
        }

        Heading.Text = account.DisplayName;
        LabelText.Text = account.Label;
        LoginText.Text = account.Username;
        NotesText.Text = account.Notes;
        PasswordHint.Text = "Leave blank to keep the current password.";
        TopRole.IsChecked = account.Roles.HasFlag(AccountRole.Top);
        JungleRole.IsChecked = account.Roles.HasFlag(AccountRole.Jungle);
        MidRole.IsChecked = account.Roles.HasFlag(AccountRole.Mid);
        BotRole.IsChecked = account.Roles.HasFlag(AccountRole.Bot);
        SupportRole.IsChecked = account.Roles.HasFlag(AccountRole.Support);
    }

    public AccountSaveRequest? Result { get; private set; }

    public bool DeleteRequested { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        string? region = RegionText.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(LoginText.Text) || string.IsNullOrWhiteSpace(region))
        {
            MessageBox.Show(this, "Username and region are required.", "Account details", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SensitiveBuffer? password = PasswordText.SecurePassword.Length > 0
            ? new SensitiveBuffer(SecurePasswordBytes.From(PasswordText.SecurePassword))
            : null;
        if (password is null && _existingAccount is null)
        {
            MessageBox.Show(this, "Enter the account password.", "Account details", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        VaultAccount account = _existingAccount ?? new VaultAccount();
        account.Label = string.IsNullOrWhiteSpace(LabelText.Text) ? null : LabelText.Text.Trim();
        account.Username = LoginText.Text.Trim();
        account.Region = LeagueRegion.Normalize(region);
        account.Notes = string.IsNullOrWhiteSpace(NotesText.Text) ? null : NotesText.Text.Trim();
        account.Roles = GetSelectedRoles();

        Result = new AccountSaveRequest(account, password);
        DialogResult = true;
    }

    private AccountRole GetSelectedRoles() =>
        (TopRole.IsChecked == true ? AccountRole.Top : AccountRole.None)
        | (JungleRole.IsChecked == true ? AccountRole.Jungle : AccountRole.None)
        | (MidRole.IsChecked == true ? AccountRole.Mid : AccountRole.None)
        | (BotRole.IsChecked == true ? AccountRole.Bot : AccountRole.None)
        | (SupportRole.IsChecked == true ? AccountRole.Support : AccountRole.None);

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        MessageBoxResult confirmation = MessageBox.Show(
            this,
            "Delete this account from the vault?",
            "Delete account",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        DeleteRequested = true;
        DialogResult = true;
    }
}
