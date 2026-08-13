using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using SummonersVault.App.Services;
using SummonersVault.Application.Accounts;
using SummonersVault.Application.Security;
using SummonersVault.Core.Models;

namespace SummonersVault.App;

public partial class AccountDialog : Window
{
    private readonly VaultAccount? _existing;
    public AccountSaveRequest? Result { get; private set; }
    public bool DeleteRequested { get; private set; }

    public AccountDialog(VaultAccount? account)
    {
        InitializeComponent(); DarkTitleBar.Attach(this);
        _existing = account;
        DeleteButton.Visibility = account is null ? Visibility.Collapsed : Visibility.Visible;
        if (account is null)
        {
            return;
        }

        Heading.Text = account.DisplayName;
        LabelText.Text = account.Label;
        LoginText.Text = account.Username;
        SelectRegion(account.Region);
        NotesText.Text = account.Notes;
        PasswordHint.Text = "Leave blank to keep the current password.";
        TopRole.IsChecked = account.Roles.HasFlag(AccountRole.Top);
        JungleRole.IsChecked = account.Roles.HasFlag(AccountRole.Jungle);
        MidRole.IsChecked = account.Roles.HasFlag(AccountRole.Mid);
        BotRole.IsChecked = account.Roles.HasFlag(AccountRole.Bot);
        SupportRole.IsChecked = account.Roles.HasFlag(AccountRole.Support);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        string? region = (RegionText.SelectedItem as ComboBoxItem)?.Content?.ToString();
        if (string.IsNullOrWhiteSpace(LoginText.Text) || string.IsNullOrWhiteSpace(region)) { MessageBox.Show(this, "Username and region are required.", "Account details", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        SensitiveBuffer? password = null;
        if (PasswordText.SecurePassword.Length > 0)
        {
            password = new SensitiveBuffer(SecurePasswordBytes.From(PasswordText.SecurePassword));
        }

        if (password is null && _existing is null) { MessageBox.Show(this, "Enter the account password.", "Account details", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        VaultAccount account = _existing ?? new VaultAccount();
        account.Label = string.IsNullOrWhiteSpace(LabelText.Text) ? null : LabelText.Text.Trim();
        account.Username = LoginText.Text.Trim(); account.Region = LeagueRegion.Normalize(region); account.Notes = string.IsNullOrWhiteSpace(NotesText.Text) ? null : NotesText.Text.Trim();
        account.Roles = (TopRole.IsChecked == true ? AccountRole.Top : 0) | (JungleRole.IsChecked == true ? AccountRole.Jungle : 0) | (MidRole.IsChecked == true ? AccountRole.Mid : 0) | (BotRole.IsChecked == true ? AccountRole.Bot : 0) | (SupportRole.IsChecked == true ? AccountRole.Support : 0);
        Result = new AccountSaveRequest(account, password); DialogResult = true;
    }

    private void SelectRegion(string region)
    {
        region = LeagueRegion.Normalize(region);
        foreach (ComboBoxItem item in RegionText.Items)
        {
            if (string.Equals(item.Content?.ToString(), region, StringComparison.OrdinalIgnoreCase)) { RegionText.SelectedItem = item; return; }
        }

        var custom = new ComboBoxItem { Content = region };
        RegionText.Items.Add(custom);
        RegionText.SelectedItem = custom;
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Delete this account from the vault?", "Delete account", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        DeleteRequested = true; DialogResult = true;
    }
}
