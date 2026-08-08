using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using SummonersVault.App.Services;
using SummonersVault.Core.Abstractions;
using SummonersVault.Core.Models;
using SummonersVault.Core.Services;
using SummonersVault.Infrastructure.League;
using SummonersVault.Infrastructure.Settings;

namespace SummonersVault.App.ViewModels;

public enum ShellState { Onboarding, Locked, Vault }

public sealed partial class MainViewModel(
    IVaultSession session,
    ILeagueClientGateway league,
    AppSettingsStore settingsStore,
    IBackupService backup,
    SafeClipboardService clipboard) : ObservableObject, IAsyncDisposable
{
    private readonly List<VaultAccount> _accounts = [];
    private CancellationTokenSource? _searchDelay;
    private Guid? _pendingLeagueAccountId;
    private bool _pendingSignInNotified;
    private bool _clientStatusUpdateInProgress;

    [ObservableProperty] private ShellState state;
    [ObservableProperty] private string query = string.Empty;
    [ObservableProperty] private bool sortRecentlyPlayed;
    [ObservableProperty] private int regionFilterIndex;
    [ObservableProperty] private int queueFilterIndex;
    [ObservableProperty] private string rankFilter = string.Empty;
    [ObservableProperty] private bool filterTop;
    [ObservableProperty] private bool filterJungle;
    [ObservableProperty] private bool filterMid;
    [ObservableProperty] private bool filterBot;
    [ObservableProperty] private bool filterSupport;
    [ObservableProperty] private string championFilter = string.Empty;
    [ObservableProperty] private string skinFilter = string.Empty;
    [ObservableProperty] private int syncFilterIndex;
    [ObservableProperty] private string statusMessage = "Preparing your vault…";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string clientStatus = "League Client not checked";
    [ObservableProperty] private bool isClientConnected;
    [ObservableProperty] private bool showEmptyVault;
    [ObservableProperty] private bool showNoFilterResults;
    [ObservableProperty] private AppSettings settings = new();
    public ObservableCollection<AccountCardViewModel> Accounts { get; } = [];
    public ObservableCollection<string> RankSuggestions { get; } = [];
    public ObservableCollection<string> ChampionSuggestions { get; } = [];
    public ObservableCollection<string> SkinSuggestions { get; } = [];
    public IBackupService Backup => backup;
    public bool IsOnboarding => State == ShellState.Onboarding;
    public bool IsLocked => State == ShellState.Locked;
    public bool IsVault => State == ShellState.Vault;

    partial void OnStateChanged(ShellState value) { OnPropertyChanged(nameof(IsOnboarding)); OnPropertyChanged(nameof(IsLocked)); OnPropertyChanged(nameof(IsVault)); }
    partial void OnQueryChanged(string value) => DebounceFilter();
    partial void OnSortRecentlyPlayedChanged(bool value) => ApplyFilter();
    partial void OnRegionFilterIndexChanged(int value) => ApplyFilter();
    partial void OnQueueFilterIndexChanged(int value) => ApplyFilter();
    partial void OnRankFilterChanged(string value) => DebounceFilter();
    partial void OnFilterTopChanged(bool value) => RoleFiltersChanged();
    partial void OnFilterJungleChanged(bool value) => RoleFiltersChanged();
    partial void OnFilterMidChanged(bool value) => RoleFiltersChanged();
    partial void OnFilterBotChanged(bool value) => RoleFiltersChanged();
    partial void OnFilterSupportChanged(bool value) => RoleFiltersChanged();
    partial void OnChampionFilterChanged(string value) => DebounceFilter();
    partial void OnSkinFilterChanged(string value) => DebounceFilter();
    partial void OnSyncFilterIndexChanged(int value) => ApplyFilter();

    public async Task InitializeAsync()
    {
        Settings = await settingsStore.LoadAsync();
        if (league is LeagueClientGateway gateway) gateway.SetConfiguredInstallDirectory(Settings.LeagueInstallDirectory);
        State = session.Exists ? ShellState.Locked : ShellState.Onboarding;
        StatusMessage = session.Exists ? "Vault locked" : "Create your local vault";
    }

    public async Task CreateAsync(byte[] password, byte[] confirmation)
    {
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(password, confirmation)) throw new ArgumentException("Master passwords do not match.");
            IsBusy = true;
            await session.CreateAsync(password);
            State = ShellState.Vault;
            StatusMessage = "Vault ready";
            await RefreshAsync();
        }
        finally { CryptographicOperations.ZeroMemory(password); CryptographicOperations.ZeroMemory(confirmation); IsBusy = false; }
    }

    public async Task<bool> UnlockAsync(byte[] password)
    {
        try
        {
            IsBusy = true;
            var unlocked = await session.UnlockAsync(password);
            if (!unlocked) { StatusMessage = "Master password is incorrect or the vault is damaged."; return false; }
            State = ShellState.Vault;
            StatusMessage = "Vault unlocked";
            await RefreshAsync();
            return true;
        }
        finally { CryptographicOperations.ZeroMemory(password); IsBusy = false; }
    }

    public async Task LockAsync()
    {
        clipboard.ClearOwned();
        _accounts.Clear(); Accounts.Clear();
        await session.LockAsync();
        State = ShellState.Locked;
        StatusMessage = "Vault locked";
        ClientStatus = "Monitoring paused while locked";
        IsClientConnected = false;
        _pendingLeagueAccountId = null;
    }

    public async Task RefreshAsync()
    {
        _accounts.Clear();
        _accounts.AddRange(await session.Repository.GetAccountsAsync());
        RefreshFilterSuggestions();
        ApplyFilter();
    }

    public async Task SaveAccountAsync(VaultAccount account)
    {
        account.ModifiedAtUtc = DateTimeOffset.UtcNow;
        await session.Repository.SaveAccountAsync(account);
        await RefreshAsync();
        StatusMessage = $"Saved {account.DisplayName}";
    }

    public async Task DeleteAsync(Guid accountId)
    {
        await session.Repository.DeleteAccountAsync(accountId);
        await RefreshAsync();
        StatusMessage = "Account removed";
    }

    public Task<VaultAccount?> GetAccountAsync(Guid accountId, bool includePassword = false) => session.Repository.GetAccountAsync(accountId, includePassword);

    public async Task CopyLoginAsync(Guid accountId)
    {
        var account = await session.Repository.GetAccountAsync(accountId);
        if (account is null) return;
        clipboard.Copy(account.LoginIdentifier);
        StatusMessage = "Username copied — clipboard clears in 30 seconds";
    }

    public async Task CopyPasswordAsync(Guid accountId)
    {
        var account = await session.Repository.GetAccountAsync(accountId, includePassword: true);
        if (account is null) return;
        try { clipboard.Copy(System.Text.Encoding.UTF8.GetString(account.PasswordUtf8)); StatusMessage = "Password copied — clipboard clears in 30 seconds"; }
        finally { CryptographicOperations.ZeroMemory(account.PasswordUtf8); }
    }

    public async Task LaunchAsync(Guid accountId)
    {
        _pendingLeagueAccountId = accountId;
        _pendingSignInNotified = false;
        var launched = await league.LaunchAsync(Settings.LeagueInstallDirectory);
        StatusMessage = launched ? "Riot Client launched — sign in and this account will sync automatically" : "Riot Client installation was not found. Set its folder in Settings.";
    }

    public async Task UpdateClientStatusAsync()
    {
        if (!IsVault || _clientStatusUpdateInProgress) return;
        _clientStatusUpdateInProgress = true;
        try
        {
            var status = await league.GetStatusAsync();
            ClientStatus = status.Message;
            IsClientConnected = status.IsLoggedIn;
            if (status.IsLoggedIn && _pendingLeagueAccountId is { } accountId)
            {
                if (!_pendingSignInNotified)
                {
                    _pendingSignInNotified = true;
                    StatusMessage = "League sign-in detected — synchronizing the selected account…";
                }

                try
                {
                    IsBusy = true;
                    var inventoryComplete = await SynchronizeAccountAsync(accountId);
                    if (inventoryComplete)
                    {
                        _pendingLeagueAccountId = null;
                        StatusMessage = "League profile synchronized";
                    }
                    else
                    {
                        StatusMessage = "League profile linked — waiting for champion and skin inventory…";
                    }
                }
                catch (LeagueIdentityConflictException ex)
                {
                    _pendingLeagueAccountId = null;
                    StatusMessage = ex.Message;
                }
                catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or HttpRequestException or TaskCanceledException)
                {
                    StatusMessage = "Automatic synchronization will retry when League is ready.";
                }
                finally { IsBusy = false; }
            }
        }
        finally { _clientStatusUpdateInProgress = false; }
    }

    public async Task SyncAsync(Guid accountId)
    {
        try
        {
            IsBusy = true;
            StatusMessage = "Reading the signed-in League account…";
            var inventoryComplete = await SynchronizeAccountAsync(accountId);
            if (inventoryComplete)
            {
                if (_pendingLeagueAccountId == accountId) _pendingLeagueAccountId = null;
                StatusMessage = "League profile synchronized";
            }
            else
            {
                StatusMessage = _pendingLeagueAccountId == accountId
                    ? "League profile linked — inventory is still loading and automatic synchronization will retry."
                    : "League profile synchronized, but inventory is still loading. Try again shortly.";
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or HttpRequestException or TaskCanceledException) { StatusMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    private async Task<bool> SynchronizeAccountAsync(Guid accountId)
    {
        var snapshot = await league.FetchCurrentSnapshotAsync();
        var existing = _accounts.FirstOrDefault(x => string.Equals(x.Puuid, snapshot.Puuid, StringComparison.Ordinal) && x.Id != accountId);
        if (existing is not null) throw new LeagueIdentityConflictException($"That League profile is already linked to {existing.DisplayName}.");
        await session.Repository.ApplyLeagueSnapshotAsync(accountId, snapshot);
        await RefreshAsync();
        return snapshot.HasCompleteInventory;
    }

    public async Task SaveSettingsAsync(AppSettings updated)
    {
        Settings = updated;
        await settingsStore.SaveAsync(updated);
        if (league is LeagueClientGateway gateway) gateway.SetConfiguredInstallDirectory(updated.LeagueInstallDirectory);
    }

    public async Task ChangeMasterPasswordAsync(byte[] current, byte[] replacement, byte[] confirmation)
    {
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(replacement, confirmation)) throw new ArgumentException("New master passwords do not match.");
            await session.ChangeMasterPasswordAsync(current, replacement);
            StatusMessage = "Master password changed";
        }
        finally { CryptographicOperations.ZeroMemory(current); CryptographicOperations.ZeroMemory(replacement); CryptographicOperations.ZeroMemory(confirmation); }
    }

    private async void DebounceFilter()
    {
        _searchDelay?.Cancel();
        _searchDelay = new CancellationTokenSource();
        try { await Task.Delay(150, _searchDelay.Token); ApplyFilter(); } catch (OperationCanceledException) { }
    }

    private void ApplyFilter()
    {
        Accounts.Clear();
        var syncState = SyncFilterIndex switch { 1 => MatchHistoryState.Known, 2 => MatchHistoryState.NeverPlayed, 3 => MatchHistoryState.Unknown, 4 => MatchHistoryState.Stale, _ => (MatchHistoryState?)null };
        var region = RegionFilterIndex switch { 1 => "EUW1", 2 => "EUN1", 3 => "NA1", 4 => "KR", 5 => "BR1", 6 => "JP1", 7 => "LA1", 8 => "LA2", 9 => "OC1", 10 => "TR1", _ => null };
        var queue = QueueFilterIndex switch { 1 => "RANKED_SOLO_5x5", 2 => "RANKED_FLEX_SR", _ => null };
        var facets = new AccountFilter(region, queue, RankFilter, SelectedRoleFilters, ChampionFilter, SkinFilter, syncState);
        foreach (var account in AccountSearch.Apply(_accounts, Query, SortRecentlyPlayed ? AccountSort.RecentlyPlayed : AccountSort.Name, facets)) Accounts.Add(new(account));
        ShowEmptyVault = _accounts.Count == 0;
        ShowNoFilterResults = _accounts.Count > 0 && Accounts.Count == 0;
    }

    public void ClearFilters()
    {
        Query = string.Empty;
        RegionFilterIndex = 0;
        QueueFilterIndex = 0;
        RankFilter = string.Empty;
        FilterTop = FilterJungle = FilterMid = FilterBot = FilterSupport = false;
        ChampionFilter = string.Empty;
        SkinFilter = string.Empty;
        SyncFilterIndex = 0;
        ApplyFilter();
    }

    public string RoleFilterSummary
    {
        get
        {
            var roles = new List<string>(5);
            if (FilterTop) roles.Add("Top");
            if (FilterJungle) roles.Add("Jungle");
            if (FilterMid) roles.Add("Mid");
            if (FilterBot) roles.Add("Bot");
            if (FilterSupport) roles.Add("Support");
            return roles.Count == 0 ? "Any role" : string.Join(", ", roles);
        }
    }

    private AccountRole SelectedRoleFilters =>
        (FilterTop ? AccountRole.Top : AccountRole.None)
        | (FilterJungle ? AccountRole.Jungle : AccountRole.None)
        | (FilterMid ? AccountRole.Mid : AccountRole.None)
        | (FilterBot ? AccountRole.Bot : AccountRole.None)
        | (FilterSupport ? AccountRole.Support : AccountRole.None);

    private void RoleFiltersChanged()
    {
        OnPropertyChanged(nameof(RoleFilterSummary));
        ApplyFilter();
    }

    private void RefreshFilterSuggestions()
    {
        var rankValues = _accounts.SelectMany(account => account.Ranks).SelectMany(rank =>
            {
                var tier = Title(rank.Tier);
                return new[] { tier, string.IsNullOrWhiteSpace(rank.Division) ? tier : $"{tier} {rank.Division}" };
            })
            .Concat(_accounts.Any(account => account.CardRank is null) ? ["Unranked"] : []);
        ReplaceSuggestions(RankSuggestions, rankValues);
        ReplaceSuggestions(ChampionSuggestions, _accounts.SelectMany(account => account.Champions).Select(champion => champion.Name));
        ReplaceSuggestions(SkinSuggestions, OwnedSkinRules.Normalize(_accounts.SelectMany(account => account.Skins)).Select(skin => skin.Name));
    }

    private static void ReplaceSuggestions(ObservableCollection<string> target, IEnumerable<string> values)
    {
        target.Clear();
        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase))
            target.Add(value);
    }

    private static string Title(string value) => string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();

    public async ValueTask DisposeAsync()
    {
        _searchDelay?.Cancel(); _searchDelay?.Dispose();
        clipboard.ClearOwned();
        await session.DisposeAsync();
    }
}

file sealed class LeagueIdentityConflictException(string message) : InvalidOperationException(message);

public sealed class AccountCardViewModel(VaultAccount account)
{
    public Guid Id => account.Id;
    public string DisplayName => account.DisplayName;
    public string RiotId => string.IsNullOrWhiteSpace(account.RiotGameName) ? "Not linked" : $"{account.RiotGameName}#{account.RiotTagLine}";
    public string Region => account.Region;
    public string Rank => account.CardRank is { } rank ? $"{Title(rank.Tier)} {rank.Division} · {rank.LeaguePoints} LP" : "Unranked";
    public string Roles => account.Roles == AccountRole.None ? "No role tags" : account.Roles.ToString().Replace(",", " ·", StringComparison.Ordinal);
    public string Notes => string.IsNullOrWhiteSpace(account.Notes) ? "No notes" : account.Notes;
    public byte[]? Icon => account.ProfileIconBytes;
    public string LastPlayed => account.MatchHistoryState switch
    {
        MatchHistoryState.NeverPlayed => "Never played",
        MatchHistoryState.Unknown => "Not synced",
        MatchHistoryState.Stale when account.LastMatchPlayedAtUtc is { } stale => $"Last played {stale.ToLocalTime():d MMM yyyy} · data may be stale",
        _ when account.LastMatchPlayedAtUtc is { } known => $"Last played {known.ToLocalTime():d MMM yyyy}",
        _ => "Not synced"
    };
    public string LastPlayedTooltip => account.LastMatchPlayedAtUtc?.ToLocalTime().ToString("F") ?? LastPlayed;
    private static string Title(string value) => string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
}
