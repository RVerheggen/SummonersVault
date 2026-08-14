using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using SummonersVault.App.Services;
using SummonersVault.Application.Abstractions;
using SummonersVault.Application.Accounts;
using SummonersVault.Application.League;
using SummonersVault.Application.Vault;
using SummonersVault.Application.Settings;
using SummonersVault.Application.Backup;
using SummonersVault.Core.Models;
using SummonersVault.Core.Services;
using SummonersVault.Application.Security;

namespace SummonersVault.App.ViewModels;

public enum ShellState { Onboarding, Locked, Vault }

public sealed partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly VaultService _vault;
    private readonly AccountService _accountService;
    private readonly LeagueSynchronizationService _leagueSynchronization;
    private readonly ILeagueClientGateway _league;
    private readonly ILeagueClientConfiguration _leagueConfiguration;
    private readonly SettingsService _settingsService;
    private readonly BackupService _backup;
    private readonly SafeClipboardService _clipboard;
    private readonly IArtworkService _artwork;
    private readonly IUpdateService _updates;
    private readonly UpdateWorkflow _updateWorkflow;
    private readonly List<VaultAccount> _accounts = [];
    private readonly Dictionary<Guid, CancellationTokenSource> _progressionSynchronizations = [];
    private readonly Dictionary<Guid, string> _synchronizedProgressionPuuids = [];
    private CancellationTokenSource? _searchDelay;
    private Guid? _pendingLeagueAccountId;
    private bool _pendingSignInNotified;
    private bool _clientStatusUpdateInProgress;

    internal MainViewModel(
        VaultService vault,
        AccountService accountService,
        LeagueSynchronizationService leagueSynchronization,
        ILeagueClientGateway league,
        ILeagueClientConfiguration leagueConfiguration,
        SettingsService settingsService,
        BackupService backup,
        SafeClipboardService clipboard,
        IArtworkService artwork,
        IUpdateService updates)
    {
        _vault = vault;
        _accountService = accountService;
        _leagueSynchronization = leagueSynchronization;
        _league = league;
        _leagueConfiguration = leagueConfiguration;
        _settingsService = settingsService;
        _backup = backup;
        _clipboard = clipboard;
        _artwork = artwork;
        _updates = updates;
        _updateWorkflow = new(updates);
    }

    [ObservableProperty]
    public partial ShellState State { get; set; }

    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool SortRecentlyPlayed { get; set; }

    [ObservableProperty]
    public partial int RegionFilterIndex { get; set; }

    [ObservableProperty]
    public partial int QueueFilterIndex { get; set; }

    [ObservableProperty]
    public partial string RankFilter { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool FilterTop { get; set; }

    [ObservableProperty]
    public partial bool FilterJungle { get; set; }

    [ObservableProperty]
    public partial bool FilterMid { get; set; }

    [ObservableProperty]
    public partial bool FilterBot { get; set; }

    [ObservableProperty]
    public partial bool FilterSupport { get; set; }

    [ObservableProperty]
    public partial string ChampionFilter { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SkinFilter { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int SyncFilterIndex { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Preparing your vault…";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string ClientStatus { get; set; } = "League Client not checked";

    [ObservableProperty]
    public partial bool IsClientConnected { get; set; }

    [ObservableProperty]
    public partial bool ShowEmptyVault { get; set; }

    [ObservableProperty]
    public partial bool ShowNoFilterResults { get; set; }

    [ObservableProperty]
    public partial string UpdateStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsCheckingForUpdates { get; set; }

    [ObservableProperty]
    public partial AppSettings Settings { get; set; } = new();
    public ObservableCollection<AccountCardViewModel> Accounts { get; } = [];
    public ObservableCollection<string> RankSuggestions { get; } = [];
    public ObservableCollection<string> ChampionSuggestions { get; } = [];
    public ObservableCollection<string> SkinSuggestions { get; } = [];
    public BackupService Backup => _backup;
    public IArtworkService Artwork => _artwork;
    public string CurrentVersion => _updates.CurrentVersion;
    public bool UpdatesAvailable => _updates.IsPackaged;
    public string LastUpdateCheck => Settings.LastUpdateCheckAtUtc is { } last ? last.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : "Never checked";
    public bool IsOnboarding => State == ShellState.Onboarding;
    public bool IsLocked => State == ShellState.Locked;
    public bool IsVault => State == ShellState.Vault;
    public bool CanAuthenticate => !IsBusy;
    public event Action<Guid>? ChampionProgressionUpdated;
    public event Action<Guid>? ChampionProgressionSynchronizationFinished;

    internal bool IsChampionProgressionSynchronizing(Guid accountId) => _progressionSynchronizations.ContainsKey(accountId);

    partial void OnStateChanged(ShellState value) { OnPropertyChanged(nameof(IsOnboarding)); OnPropertyChanged(nameof(IsLocked)); OnPropertyChanged(nameof(IsVault)); }
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanAuthenticate));
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
        Settings = await _settingsService.LoadAsync();
        UpdateStatus = _updates.IsPackaged ? "Ready to check for updates" : "Updates unavailable in development builds";
        OnPropertyChanged(nameof(LastUpdateCheck));
        _leagueConfiguration.SetInstallDirectory(Settings.LeagueInstallDirectory);

        State = _vault.Exists ? ShellState.Locked : ShellState.Onboarding;
        StatusMessage = string.Empty;
    }

    public async Task CreateAsync(byte[] password, byte[] confirmation)
    {
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(password, confirmation))
            {
                throw new ArgumentException("Master passwords do not match.");
            }

            IsBusy = true;
            await _vault.CreateAsync(password);
            State = ShellState.Vault;
            await RefreshAsync();
            StatusMessage = string.Empty;
        }
        finally { CryptographicOperations.ZeroMemory(password); CryptographicOperations.ZeroMemory(confirmation); IsBusy = false; }
    }

    public async Task<bool> UnlockAsync(byte[] password)
    {
        if (_vault.IsUnlocked || State == ShellState.Vault)
        {
            CryptographicOperations.ZeroMemory(password);
            return true;
        }

        if (IsBusy)
        {
            CryptographicOperations.ZeroMemory(password);
            return false;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Unlocking vault...";
            bool unlocked;
            try
            {
                unlocked = await _vault.UnlockAsync(password);
            }
            catch (UnsupportedVaultException exception)
            {
                StatusMessage = exception.Message;
                return false;
            }
            catch (VaultUpgradeException exception)
            {
                StatusMessage = exception.Message;
                return false;
            }
            if (!unlocked)
            {
                if (!_vault.IsUnlocked && State == ShellState.Locked)
                {
                    StatusMessage = "Master password is incorrect or the vault is damaged.";
                }

                return false;
            }

            State = ShellState.Vault;
            long accountLoadStartedAt = Stopwatch.GetTimestamp();
            await RefreshAsync();
            Debug.WriteLine($"SummonersVault unlock timing: account loading and UI mapping={Stopwatch.GetElapsedTime(accountLoadStartedAt).TotalMilliseconds:F0} ms");
            StatusMessage = string.Empty;
            return true;
        }
        finally { CryptographicOperations.ZeroMemory(password); IsBusy = false; }
    }

    public async Task LockAsync()
    {
        CancelProgressionSynchronizations();
        _clipboard.ClearOwned();
        _accounts.Clear(); Accounts.Clear();
        await _vault.LockAsync();
        State = ShellState.Locked;
        StatusMessage = string.Empty;
        ClientStatus = "Monitoring paused while locked";
        IsClientConnected = false;
        _pendingLeagueAccountId = null;
    }

    public async Task RefreshAsync()
    {
        _accounts.Clear();
        _accounts.AddRange(await _accountService.GetAllAsync());
        RefreshFilterSuggestions();
        ApplyFilter();
    }

    public async Task SaveAccountAsync(AccountSaveRequest request)
    {
        try
        {
            await _accountService.SaveAsync(request);
            await RefreshAsync();
            StatusMessage = $"Saved {request.Account.DisplayName}";
        }
        finally
        {
            request.Password?.Dispose();
        }
    }

    public async Task DeleteAsync(Guid accountId)
    {
        if (_progressionSynchronizations.Remove(accountId, out CancellationTokenSource? cancellation))
        {
            cancellation.Cancel();
        }

        _synchronizedProgressionPuuids.Remove(accountId);
        await _accountService.DeleteAsync(accountId);
        await RefreshAsync();
        StatusMessage = "Account removed";
    }

    public Task<VaultAccount?> GetAccountAsync(Guid accountId) => _accountService.GetAsync(accountId);

    public async Task CopyLoginAsync(Guid accountId)
    {
        VaultAccount? account = await _accountService.GetAsync(accountId);
        if (account is null)
        {
            return;
        }

        _clipboard.Copy(account.Username);
        StatusMessage = "Username copied - clipboard clears in 30 seconds";
    }

    public async Task CopyPasswordAsync(Guid accountId)
    {
        using SensitiveBuffer? password = await _accountService.GetPasswordAsync(accountId);
        if (password is null)
        {
            return;
        }

        _clipboard.Copy(System.Text.Encoding.UTF8.GetString(password.Memory.Span));
        StatusMessage = "Password copied - clipboard clears in 30 seconds";
    }

    public async Task LaunchAsync(Guid accountId)
    {
        _pendingLeagueAccountId = accountId;
        _pendingSignInNotified = false;
        _synchronizedProgressionPuuids.Remove(accountId);
        bool launched = await _league.LaunchAsync(Settings.LeagueInstallDirectory);
        StatusMessage = launched ? "Riot Client launched - sign in and this account will sync automatically" : "Riot Client installation was not found. Set its folder in Settings.";
    }

    public async Task UpdateClientStatusAsync()
    {
        if (!IsVault || _clientStatusUpdateInProgress)
        {
            return;
        }

        _clientStatusUpdateInProgress = true;
        try
        {
            LeagueClientStatus status = await _league.GetStatusAsync();
            ClientStatus = status.Message;
            IsClientConnected = status.IsLoggedIn;
            if (status.IsLoggedIn && _pendingLeagueAccountId is { } accountId)
            {
                if (!_pendingSignInNotified)
                {
                    _pendingSignInNotified = true;
                    StatusMessage = "League sign-in detected - synchronizing the selected account…";
                }

                try
                {
                    IsBusy = true;
                    LeagueSnapshot snapshot = await SynchronizeAccountAsync(accountId, forceProgression: false);
                    if (snapshot.HasCompleteSyncData)
                    {
                        _pendingLeagueAccountId = null;
                        StatusMessage = _progressionSynchronizations.ContainsKey(accountId)
                            ? "League profile synchronized - champion progression is updating in the background"
                            : "League profile synchronized";
                    }
                    else
                    {
                        StatusMessage = $"League profile linked - waiting for {DescribeMissingSyncData(snapshot)}…";
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

    public async Task<string?> SyncAsync(Guid accountId)
    {
        string? errorMessage = null;
        try
        {
            IsBusy = true;
            StatusMessage = "Reading the signed-in League account…";
            LeagueSnapshot snapshot = await SynchronizeAccountAsync(accountId, forceProgression: true);
            if (snapshot.HasCompleteSyncData)
            {
                if (_pendingLeagueAccountId == accountId)
                {
                    _pendingLeagueAccountId = null;
                }

                StatusMessage = _progressionSynchronizations.ContainsKey(accountId)
                    ? "League profile synchronized - champion progression is updating in the background"
                    : "League profile synchronized";
            }
            else
            {
                StatusMessage = _pendingLeagueAccountId == accountId
                    ? $"League profile linked - {DescribeMissingSyncData(snapshot)} is still loading and automatic synchronization will retry."
                    : $"League profile synchronized, but {DescribeMissingSyncData(snapshot)} is still loading. Try again shortly.";
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or HttpRequestException or TaskCanceledException)
        {
            errorMessage = ex.Message;
            StatusMessage = errorMessage;
        }
        finally { IsBusy = false; }
        return errorMessage;
    }

    private async Task<LeagueSnapshot> SynchronizeAccountAsync(Guid accountId, bool forceProgression)
    {
        LeagueSnapshot snapshot = await _leagueSynchronization.SynchronizeAsync(accountId);
        await RefreshAsync();
        StartChampionProgressionSynchronization(accountId, snapshot.Puuid, forceProgression);
        return snapshot;
    }

    private void StartChampionProgressionSynchronization(Guid accountId, string puuid, bool forceProgression)
    {
        if (!IsVault
            || _progressionSynchronizations.ContainsKey(accountId)
            || !forceProgression
            && _synchronizedProgressionPuuids.TryGetValue(accountId, out string? synchronizedPuuid)
            && string.Equals(synchronizedPuuid, puuid, StringComparison.Ordinal))
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _progressionSynchronizations.Add(accountId, cancellation);
        _ = SynchronizeChampionProgressionInBackgroundAsync(accountId, puuid, cancellation);
    }

    private async Task SynchronizeChampionProgressionInBackgroundAsync(
        Guid accountId,
        string puuid,
        CancellationTokenSource cancellation)
    {
        try
        {
            ChampionProgressionSnapshot snapshot = await _leagueSynchronization.SynchronizeChampionProgressionAsync(
                accountId,
                puuid,
                cancellation.Token);
            if (!IsVault || cancellation.IsCancellationRequested)
            {
                return;
            }

            await RefreshAsync();
            _synchronizedProgressionPuuids[accountId] = puuid;
            ChampionProgressionUpdated?.Invoke(accountId);
            StatusMessage = snapshot.IsComplete
                ? "League profile and champion progression synchronized"
                : "League profile synchronized, but some champion progression is still loading. Try again shortly.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or HttpRequestException or TaskCanceledException)
        {
            if (IsVault && !cancellation.IsCancellationRequested)
            {
                StatusMessage = "League profile synchronized, but champion progression could not be refreshed.";
            }
        }
        finally
        {
            if (_progressionSynchronizations.TryGetValue(accountId, out CancellationTokenSource? current)
                && ReferenceEquals(current, cancellation))
            {
                _progressionSynchronizations.Remove(accountId);
            }

            ChampionProgressionSynchronizationFinished?.Invoke(accountId);
            cancellation.Dispose();
        }
    }

    private void CancelProgressionSynchronizations()
    {
        foreach (CancellationTokenSource cancellation in _progressionSynchronizations.Values)
        {
            cancellation.Cancel();
        }

        _progressionSynchronizations.Clear();
        _synchronizedProgressionPuuids.Clear();
    }

    private static string DescribeMissingSyncData(LeagueSnapshot snapshot)
    {
        var missing = new List<string>(5);
        if (snapshot.Ranks is null)
        {
            missing.Add("ranked data");
        }

        if (snapshot.Champions is null)
        {
            missing.Add("champion inventory");
        }

        if (snapshot.Skins is null)
        {
            missing.Add("skin inventory");
        }

        if (snapshot.Wallet is not { RiotPoints: not null, BlueEssence: not null })
        {
            missing.Add("wallet data");
        }

        if (snapshot.CraftingLoot is null)
        {
            missing.Add("crafting inventory");
        }

        return missing.Count == 0 ? "League data" : string.Join(", ", missing);
    }

    public async Task SaveSettingsAsync(AppSettings updated)
    {
        Settings = updated;
        await _settingsService.SaveAsync(updated);
        _leagueConfiguration.SetInstallDirectory(updated.LeagueInstallDirectory);
    }

    internal bool ShouldRunAutomaticUpdateCheck(DateTimeOffset nowUtc) =>
        _updateWorkflow.ShouldRunAutomaticCheck(Settings, nowUtc);

    internal async Task<UpdateCheckResult> CheckForUpdatesAsync(bool manual, CancellationToken cancellationToken = default)
    {
        if (!manual && !ShouldRunAutomaticUpdateCheck(DateTimeOffset.UtcNow))
        {
            return new(UpdateCheckState.Unavailable, UpdateStatus);
        }

        IsCheckingForUpdates = true;
        UpdateStatus = "Checking for updates...";
        try
        {
            UpdateCheckResult result = await _updateWorkflow.CheckAsync(
                Settings,
                manual,
                DateTimeOffset.UtcNow,
                token => _settingsService.SaveAsync(Settings, token),
                cancellationToken);
            UpdateStatus = result.Message;
            if (result.Succeeded)
            {
                OnPropertyChanged(nameof(LastUpdateCheck));
            }
            return result;
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    internal Task<UpdateDownloadResult> DownloadUpdateAsync(AvailableUpdate update, IProgress<int> progress, CancellationToken cancellationToken) =>
        _updates.DownloadAsync(update, progress, cancellationToken);

    internal async Task PrepareForUpdateAsync()
    {
        if (IsVault)
        {
            await LockAsync();
        }
        else
        {
            _clipboard.ClearOwned();
        }
    }

    internal void ApplyUpdateAndRestart(AvailableUpdate update) => _updates.ApplyAndRestart(update);

    public async Task ChangeMasterPasswordAsync(byte[] current, byte[] replacement, byte[] confirmation)
    {
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(replacement, confirmation))
            {
                throw new ArgumentException("New master passwords do not match.");
            }

            await _vault.ChangeMasterPasswordAsync(current, replacement);
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
        MatchHistoryState? syncState = SyncFilterIndex switch { 1 => MatchHistoryState.Known, 2 => MatchHistoryState.NeverPlayed, 3 => MatchHistoryState.Unknown, 4 => MatchHistoryState.Stale, _ => (MatchHistoryState?)null };
        string? region = RegionFilterIndex switch { 1 => "EUW", 2 => "EUNE", 3 => "NA", 4 => "KR", 5 => "BR", 6 => "JP", 7 => "LAN", 8 => "LAS", 9 => "OCE", 10 => "TR", _ => null };
        string? queue = QueueFilterIndex switch { 1 => "RANKED_SOLO_5x5", 2 => "RANKED_FLEX_SR", _ => null };
        var facets = new AccountFilter(region, queue, RankFilter, SelectedRoleFilters, ChampionFilter, SkinFilter, syncState);
        foreach (VaultAccount account in AccountSearch.Apply(_accounts, Query, SortRecentlyPlayed ? AccountSort.RecentlyPlayed : AccountSort.Name, facets))
        {
            Accounts.Add(new(account));
        }

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
            if (FilterTop)
            {
                roles.Add("Top");
            }

            if (FilterJungle)
            {
                roles.Add("Jungle");
            }

            if (FilterMid)
            {
                roles.Add("Mid");
            }

            if (FilterBot)
            {
                roles.Add("Bot");
            }

            if (FilterSupport)
            {
                roles.Add("Support");
            }

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
        IEnumerable<string> rankValues = _accounts.SelectMany(account => account.Ranks).SelectMany(rank =>
            {
                string tier = Title(rank.Tier);
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
        foreach (string? value in values.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase))
        {
            target.Add(value);
        }
    }

    private static string Title(string value) => string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();

    public async ValueTask DisposeAsync()
    {
        CancelProgressionSynchronizations();
        _searchDelay?.Cancel(); _searchDelay?.Dispose();
        _clipboard.ClearOwned();
        if (_vault.IsUnlocked)
        {
            await _vault.LockAsync();
        }

    }
}

public sealed class AccountCardViewModel(VaultAccount account)
{
    public Guid Id => account.Id;
    public string DisplayName => account.DisplayName;
    public string RiotId => string.IsNullOrWhiteSpace(account.RiotGameName) ? "Not linked" : $"{account.RiotGameName}#{account.RiotTagLine}";
    public string Region => account.Region;
    public string Rank => account.CardRank is { } rank ? $"{Title(rank.Tier)} {rank.Division} · {rank.LeaguePoints} LP" : "Unranked";
    public Uri? RankIcon => RankIconCatalog.GetUri(account.CardRank?.Tier);
    public bool HasRankIcon => RankIcon is not null;
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
    public string LastPlayedTooltip => account.LastMatchPlayedAtUtc?.ToLocalTime().ToString("F", CultureInfo.CurrentCulture) ?? LastPlayed;
    private static string Title(string value) => string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
}
