using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using SummonersVault.App.Services;
using SummonersVault.Core.Models;

namespace SummonersVault.App;

public partial class AccountDialog : Window
{
    private readonly VaultAccount? _existing;
    public VaultAccount? Result { get; private set; }
    public bool DeleteRequested { get; private set; }

    public AccountDialog(VaultAccount? account)
    {
        InitializeComponent(); DarkTitleBar.Attach(this);
        _existing = account;
        DeleteButton.Visibility = account is null ? Visibility.Collapsed : Visibility.Visible;
        LeaguePanel.Visibility = account?.Puuid is null ? Visibility.Collapsed : Visibility.Visible;
        if (account is null) return;
        Heading.Text = account.DisplayName;
        LabelText.Text = account.Label;
        LoginText.Text = account.LoginIdentifier;
        SelectRegion(account.Region);
        NotesText.Text = account.Notes;
        PasswordHint.Text = "Leave blank to keep the current password.";
        TopRole.IsChecked = account.Roles.HasFlag(AccountRole.Top);
        JungleRole.IsChecked = account.Roles.HasFlag(AccountRole.Jungle);
        MidRole.IsChecked = account.Roles.HasFlag(AccountRole.Mid);
        BotRole.IsChecked = account.Roles.HasFlag(AccountRole.Bot);
        SupportRole.IsChecked = account.Roles.HasFlag(AccountRole.Support);
        LeagueIdentity.Text = string.IsNullOrWhiteSpace(account.RiotGameName) ? "Linked League profile" : $"{account.RiotGameName}#{account.RiotTagLine} · level {account.SummonerLevel}";
        LeagueSummary.Text = account.Ranks.Count == 0 ? "Unranked" : string.Join(" · ", account.Ranks.Select(x => $"{x.QueueType.Replace("RANKED_", string.Empty, StringComparison.Ordinal)}: {x.Tier} {x.Division} {x.LeaguePoints} LP"));
        if (RankIconCatalog.GetUri(account.CardRank?.Tier) is { } rankIcon)
        {
            RankIconImage.Source = new System.Windows.Media.Imaging.BitmapImage(rankIcon);
            RankIconImage.Visibility = Visibility.Visible;
            RankIconImage.ToolTip = account.CardRank is { } rank ? $"{rank.Tier} {rank.Division} · {rank.LeaguePoints} LP" : null;
            System.Windows.Automation.AutomationProperties.SetName(RankIconImage, RankIconImage.ToolTip?.ToString() ?? "Rank icon");
        }
        RiotPointsValue.Text = FormatCurrencyValue(account.RiotPoints);
        BlueEssenceValue.Text = FormatCurrencyValue(account.BlueEssence);
        MatchSummary.Text = account.MatchHistoryState switch { MatchHistoryState.NeverPlayed => "Never played", MatchHistoryState.Unknown => "Match history not synced", MatchHistoryState.Stale => $"Last played {account.LastMatchPlayedAtUtc?.ToLocalTime():f} · data may be stale", _ => $"Last played {account.LastMatchPlayedAtUtc?.ToLocalTime():f}" };
        var countedSkins = OwnedSkinRules.Normalize(account.Skins);
        OwnershipSummary.Text = $"{account.Champions.Count} champions · {countedSkins.Count} skins owned · synced {account.LastSyncedAtUtc?.ToLocalTime():g}";
        ChampionNames.Text = account.Champions.Count == 0 ? "No champion snapshot" : string.Join(", ", account.Champions.Select(x => x.Name));
        SkinNames.Text = countedSkins.Count == 0 ? "No skin snapshot" : string.Join(", ", countedSkins.Select(x => x.Name));
    }

    private static string FormatCurrencyValue(long? value) =>
        value.HasValue ? value.Value.ToString("N0") : "Not synced";

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var region = (RegionText.SelectedItem as ComboBoxItem)?.Content?.ToString();
        if (string.IsNullOrWhiteSpace(LoginText.Text) || string.IsNullOrWhiteSpace(region)) { MessageBox.Show(this, "Username and region are required.", "Account details", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        byte[] password = [];
        if (PasswordText.SecurePassword.Length > 0) password = SecurePasswordBytes.From(PasswordText.SecurePassword);
        else if (_existing is not null) password = _existing.PasswordUtf8;
        if (password.Length == 0) { MessageBox.Show(this, "Enter the account password.", "Account details", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        var account = _existing ?? new VaultAccount();
        account.Label = string.IsNullOrWhiteSpace(LabelText.Text) ? null : LabelText.Text.Trim();
        account.LoginIdentifier = LoginText.Text.Trim(); account.Region = LeagueRegion.Normalize(region); account.Notes = string.IsNullOrWhiteSpace(NotesText.Text) ? null : NotesText.Text.Trim(); account.PasswordUtf8 = password;
        account.Roles = (TopRole.IsChecked == true ? AccountRole.Top : 0) | (JungleRole.IsChecked == true ? AccountRole.Jungle : 0) | (MidRole.IsChecked == true ? AccountRole.Mid : 0) | (BotRole.IsChecked == true ? AccountRole.Bot : 0) | (SupportRole.IsChecked == true ? AccountRole.Support : 0);
        Result = account; DialogResult = true;
    }

    private void SelectRegion(string region)
    {
        region = LeagueRegion.Normalize(region);
        foreach (ComboBoxItem item in RegionText.Items)
            if (string.Equals(item.Content?.ToString(), region, StringComparison.OrdinalIgnoreCase)) { RegionText.SelectedItem = item; return; }
        var custom = new ComboBoxItem { Content = region };
        RegionText.Items.Add(custom);
        RegionText.SelectedItem = custom;
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Delete this account from the vault?", "Delete account", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        DeleteRequested = true; DialogResult = true;
    }
}
