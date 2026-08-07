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

    [ObservableProperty] private ShellState state;
    [ObservableProperty] private string query = string.Empty;
    [ObservableProperty] private bool sortRecentlyPlayed;
    [ObservableProperty] private string regionFilter = string.Empty;
    [ObservableProperty] private string rankFilter = string.Empty;
    [ObservableProperty] private string roleFilter = string.Empty;
    [ObservableProperty] private string championFilter = string.Empty;
    [ObservableProperty] private string skinFilter = string.Empty;
    [ObservableProperty] private int syncFilterIndex;
    [ObservableProperty] private string statusMessage = "Preparing your vault…";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string clientStatus = "League Client not checked";
    [ObservableProperty] private AppSettings settings = new();
    public ObservableCollection<AccountCardViewModel> Accounts { get; } = [];
    public IBackupService Backup => backup;
    public bool IsOnboarding => State == ShellState.Onboarding;
    public bool IsLocked => State == ShellState.Locked;
    public bool IsVault => State == ShellState.Vault;

    partial void OnStateChanged(ShellState value) { OnPropertyChanged(nameof(IsOnboarding)); OnPropertyChanged(nameof(IsLocked)); OnPropertyChanged(nameof(IsVault)); }
    partial void OnQueryChanged(string value) => DebounceFilter();
    partial void OnSortRecentlyPlayedChanged(bool value) => ApplyFilter();
    partial void OnRegionFilterChanged(string value) => DebounceFilter();
    partial void OnRankFilterChanged(string value) => DebounceFilter();
    partial void OnRoleFilterChanged(string value) => DebounceFilter();
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
        _pendingLeagueAccountId = null;
    }

    public async Task RefreshAsync()
    {
        _accounts.Clear();
        _accounts.AddRange(await session.Repository.GetAccountsAsync());
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
        StatusMessage = launched ? "League Client launched — sign in, then choose Sync" : "League installation was not found. Set its folder in Settings.";
    }

    public async Task UpdateClientStatusAsync()
    {
        if (!IsVault) return;
        var status = await league.GetStatusAsync();
        ClientStatus = status.Message;
        if (status.IsLoggedIn && _pendingLeagueAccountId.HasValue && !_pendingSignInNotified)
        {
            _pendingSignInNotified = true;
            StatusMessage = "League sign-in detected — choose Sync on the selected account";
        }
    }

    public async Task SyncAsync(Guid accountId)
    {
        try
        {
            IsBusy = true;
            StatusMessage = "Reading the signed-in League account…";
            var snapshot = await league.FetchCurrentSnapshotAsync();
            var existing = _accounts.FirstOrDefault(x => string.Equals(x.Puuid, snapshot.Puuid, StringComparison.Ordinal) && x.Id != accountId);
            if (existing is not null) throw new InvalidOperationException($"That League profile is already linked to {existing.DisplayName}.");
            await session.Repository.ApplyLeagueSnapshotAsync(accountId, snapshot);
            if (_pendingLeagueAccountId == accountId) _pendingLeagueAccountId = null;
            await RefreshAsync();
            StatusMessage = "League profile synchronized";
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or HttpRequestException) { StatusMessage = ex.Message; }
        finally { IsBusy = false; }
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
        var facets = new AccountFilter(RegionFilter, RankFilter, RoleFilter, ChampionFilter, SkinFilter, syncState);
        foreach (var account in AccountSearch.Apply(_accounts, Query, SortRecentlyPlayed ? AccountSort.RecentlyPlayed : AccountSort.Name, facets)) Accounts.Add(new(account));
    }

    public async ValueTask DisposeAsync()
    {
        _searchDelay?.Cancel(); _searchDelay?.Dispose();
        clipboard.ClearOwned();
        await session.DisposeAsync();
    }
}

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
