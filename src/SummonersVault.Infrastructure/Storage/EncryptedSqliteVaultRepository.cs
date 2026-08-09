using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using SummonersVault.Core.Abstractions;
using SummonersVault.Core.Models;

namespace SummonersVault.Infrastructure.Storage;

public sealed class EncryptedSqliteVaultRepository(VaultPaths paths) : IVaultRepository
{
    private static int _sqliteInitialized;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SqliteConnection? _connection;

    public bool IsOpen => _connection is not null;
    public string DatabasePath => paths.DatabasePath;

    public async Task OpenAsync(ReadOnlyMemory<byte> databaseKey, bool create, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection is not null) return;
            paths.EnsureCreated();
            if (!create && !File.Exists(paths.DatabasePath)) throw new FileNotFoundException("Vault database was not found.", paths.DatabasePath);
            if (Interlocked.Exchange(ref _sqliteInitialized, 1) == 0) SQLitePCL.Batteries_V2.Init();

            var normalizedPath = Path.GetFullPath(paths.DatabasePath).Replace('\\', '/');
            var rawKey = $"x'{Convert.ToHexString(databaseKey.Span)}'";
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = $"file:{normalizedPath}?cipher=sqlcipher&legacy=4",
                Password = rawKey,
                Pooling = false
            }.ToString();
            var connection = new SqliteConnection(connectionString);
            try
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                await ExecuteAsync(connection, null, "PRAGMA foreign_keys=ON; PRAGMA secure_delete=ON; PRAGMA temp_store=MEMORY; PRAGMA synchronous=FULL; PRAGMA journal_mode=DELETE;", cancellationToken).ConfigureAwait(false);
                await InitializeSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
                _connection = connection;
            }
            catch
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CloseAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_connection is null) return;
            await _connection.CloseAsync().ConfigureAwait(false);
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<VaultAccount>> GetAccountsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connection = RequireConnection();
            var accounts = new List<VaultAccount>();
            await using var command = connection.CreateCommand();
            command.CommandText = AccountSelect + " ORDER BY COALESCE(NULLIF(label,''), NULLIF(riot_game_name,''), login_identifier) COLLATE NOCASE";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) accounts.Add(ReadAccount(reader, includePassword: false));
            await reader.DisposeAsync().ConfigureAwait(false);
            foreach (var account in accounts) await LoadCollectionsAsync(connection, account, cancellationToken).ConfigureAwait(false);
            return accounts;
        }
        finally { _gate.Release(); }
    }

    public async Task<VaultAccount?> GetAccountAsync(Guid id, bool includePassword = false, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connection = RequireConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = AccountSelect + " WHERE id=$id";
            command.Parameters.AddWithValue("$id", id.ToString("D"));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
            var account = ReadAccount(reader, includePassword);
            await reader.DisposeAsync().ConfigureAwait(false);
            await LoadCollectionsAsync(connection, account, cancellationToken).ConfigureAwait(false);
            return account;
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAccountAsync(VaultAccount account, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(account.LoginIdentifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(account.Region);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await SaveAccountCoreAsync(RequireConnection(), account, cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public async Task MergeAccountsAsync(IReadOnlyList<VaultAccount> accounts, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connection = RequireConnection();
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            foreach (var account in accounts) await SaveAccountCoreAsync(connection, account, cancellationToken, transaction).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteAccountAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ExecuteAsync(RequireConnection(), null, "DELETE FROM accounts WHERE id=$id", cancellationToken, ("$id", id.ToString("D"))).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task ApplyLeagueSnapshotAsync(Guid accountId, LeagueSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connection = RequireConnection();
            var account = await GetAccountCoreAsync(connection, accountId, includePassword: true, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Account no longer exists.");
            account.Puuid = snapshot.Puuid;
            account.SummonerId = snapshot.SummonerId;
            account.RiotGameName = snapshot.RiotGameName;
            account.RiotTagLine = snapshot.RiotTagLine;
            account.Region = snapshot.Region;
            account.ProfileIconId = snapshot.ProfileIconId;
            if (snapshot.ProfileIconBytes is not null) account.ProfileIconBytes = snapshot.ProfileIconBytes;
            account.SummonerLevel = snapshot.SummonerLevel;
            if (snapshot.Wallet?.RiotPoints is { } riotPoints) account.RiotPoints = riotPoints;
            if (snapshot.Wallet?.BlueEssence is { } blueEssence) account.BlueEssence = blueEssence;
            account.LastSyncedAtUtc = DateTimeOffset.UtcNow;
            account.ModifiedAtUtc = DateTimeOffset.UtcNow;
            if (snapshot.Ranks is not null) { account.Ranks.Clear(); account.Ranks.AddRange(snapshot.Ranks); }
            if (snapshot.Champions is not null) { account.Champions.Clear(); account.Champions.AddRange(snapshot.Champions); }
            if (snapshot.Skins is not null) { account.Skins.Clear(); account.Skins.AddRange(OwnedSkinRules.Normalize(snapshot.Skins)); }

            if (snapshot.Match.Succeeded)
            {
                account.MatchHistorySyncedAtUtc = DateTimeOffset.UtcNow;
                if (snapshot.Match.HasMatch && snapshot.Match.PlayedAtUtc is { } played && (!account.LastMatchPlayedAtUtc.HasValue || played >= account.LastMatchPlayedAtUtc.Value))
                {
                    account.LastMatchPlayedAtUtc = played;
                    account.LastMatchId = snapshot.Match.MatchId;
                    account.MatchHistoryState = MatchHistoryState.Known;
                }
                else if (!account.LastMatchPlayedAtUtc.HasValue)
                {
                    account.MatchHistoryState = MatchHistoryState.NeverPlayed;
                }
            }
            else if (account.LastMatchPlayedAtUtc.HasValue)
            {
                account.MatchHistoryState = MatchHistoryState.Stale;
            }

            await SaveAccountCoreAsync(connection, account, cancellationToken).ConfigureAwait(false);
            CryptographicOperations.ZeroMemory(account.PasswordUtf8);
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    private async Task SaveAccountCoreAsync(SqliteConnection connection, VaultAccount account, CancellationToken cancellationToken, SqliteTransaction? existingTransaction = null)
    {
        await using var ownedTransaction = existingTransaction is null ? (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false) : null;
        var transaction = existingTransaction ?? ownedTransaction!;
        const string sql = """
            INSERT INTO accounts(id,login_identifier,password,label,region,notes,roles,created_at,modified_at,puuid,summoner_id,riot_game_name,riot_tag_line,profile_icon_id,profile_icon,summoner_level,last_synced_at,last_match_played_at,last_match_id,match_history_synced_at,match_history_state,riot_points,blue_essence)
            VALUES($id,$login,$password,$label,$region,$notes,$roles,$created,$modified,$puuid,$summonerId,$riotName,$tag,$iconId,$icon,$level,$synced,$lastPlayed,$matchId,$matchSynced,$matchState,$riotPoints,$blueEssence)
            ON CONFLICT(id) DO UPDATE SET login_identifier=excluded.login_identifier,password=excluded.password,label=excluded.label,region=excluded.region,notes=excluded.notes,roles=excluded.roles,modified_at=excluded.modified_at,puuid=excluded.puuid,summoner_id=excluded.summoner_id,riot_game_name=excluded.riot_game_name,riot_tag_line=excluded.riot_tag_line,profile_icon_id=excluded.profile_icon_id,profile_icon=excluded.profile_icon,summoner_level=excluded.summoner_level,last_synced_at=excluded.last_synced_at,last_match_played_at=excluded.last_match_played_at,last_match_id=excluded.last_match_id,match_history_synced_at=excluded.match_history_synced_at,match_history_state=excluded.match_history_state,riot_points=excluded.riot_points,blue_essence=excluded.blue_essence
            """;
        await ExecuteAsync(connection, transaction, sql, cancellationToken,
            ("$id", account.Id.ToString("D")), ("$login", account.LoginIdentifier), ("$password", account.PasswordUtf8), ("$label", account.Label),
            ("$region", account.Region), ("$notes", account.Notes), ("$roles", (int)account.Roles), ("$created", Format(account.CreatedAtUtc)), ("$modified", Format(account.ModifiedAtUtc)),
            ("$puuid", account.Puuid), ("$summonerId", account.SummonerId), ("$riotName", account.RiotGameName), ("$tag", account.RiotTagLine),
            ("$iconId", account.ProfileIconId), ("$icon", account.ProfileIconBytes), ("$level", account.SummonerLevel), ("$synced", Format(account.LastSyncedAtUtc)),
            ("$lastPlayed", Format(account.LastMatchPlayedAtUtc)), ("$matchId", account.LastMatchId), ("$matchSynced", Format(account.MatchHistorySyncedAtUtc)), ("$matchState", (int)account.MatchHistoryState),
            ("$riotPoints", account.RiotPoints), ("$blueEssence", account.BlueEssence)).ConfigureAwait(false);

        foreach (var table in new[] { "ranks", "champions", "skins" })
            await ExecuteAsync(connection, transaction, $"DELETE FROM {table} WHERE account_id=$id", cancellationToken, ("$id", account.Id.ToString("D"))).ConfigureAwait(false);
        foreach (var rank in account.Ranks)
            await ExecuteAsync(connection, transaction, "INSERT INTO ranks(account_id,queue_type,tier,division,league_points,wins,losses) VALUES($id,$queue,$tier,$division,$lp,$wins,$losses)", cancellationToken,
                ("$id", account.Id.ToString("D")), ("$queue", rank.QueueType), ("$tier", rank.Tier), ("$division", rank.Division), ("$lp", rank.LeaguePoints), ("$wins", rank.Wins), ("$losses", rank.Losses)).ConfigureAwait(false);
        foreach (var champion in account.Champions)
            await ExecuteAsync(connection, transaction, "INSERT INTO champions(account_id,champion_id,name) VALUES($id,$championId,$name)", cancellationToken,
                ("$id", account.Id.ToString("D")), ("$championId", champion.ChampionId), ("$name", champion.Name)).ConfigureAwait(false);
        foreach (var skin in OwnedSkinRules.Normalize(account.Skins))
            await ExecuteAsync(connection, transaction, "INSERT INTO skins(account_id,skin_id,champion_id,name) VALUES($id,$skinId,$championId,$name)", cancellationToken,
                ("$id", account.Id.ToString("D")), ("$skinId", skin.SkinId), ("$championId", skin.ChampionId), ("$name", skin.Name)).ConfigureAwait(false);
        if (ownedTransaction is not null) await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<VaultAccount?> GetAccountCoreAsync(SqliteConnection connection, Guid id, bool includePassword, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = AccountSelect + " WHERE id=$id";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        var account = ReadAccount(reader, includePassword);
        await reader.DisposeAsync().ConfigureAwait(false);
        await LoadCollectionsAsync(connection, account, cancellationToken).ConfigureAwait(false);
        return account;
    }

    private static async Task LoadCollectionsAsync(SqliteConnection connection, VaultAccount account, CancellationToken cancellationToken)
    {
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT queue_type,tier,division,league_points,wins,losses FROM ranks WHERE account_id=$id";
            command.Parameters.AddWithValue("$id", account.Id.ToString("D"));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) account.Ranks.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5)));
        }
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT champion_id,name FROM champions WHERE account_id=$id";
            command.Parameters.AddWithValue("$id", account.Id.ToString("D"));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) account.Champions.Add(new(reader.GetInt32(0), reader.GetString(1)));
        }
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT skin_id,champion_id,name FROM skins WHERE account_id=$id";
            command.Parameters.AddWithValue("$id", account.Id.ToString("D"));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var skins = new List<OwnedSkin>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                skins.Add(new OwnedSkin(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2)));
            account.Skins.AddRange(OwnedSkinRules.Normalize(skins));
        }
    }

    private static VaultAccount ReadAccount(SqliteDataReader reader, bool includePassword) => new()
    {
        Id = Guid.Parse(reader.GetString(0)), LoginIdentifier = reader.GetString(1), PasswordUtf8 = includePassword ? (byte[])reader[2] : [], Label = NullableString(reader, 3), Region = reader.GetString(4), Notes = NullableString(reader, 5), Roles = (AccountRole)reader.GetInt32(6),
        CreatedAtUtc = ParseDate(reader, 7) ?? DateTimeOffset.UtcNow, ModifiedAtUtc = ParseDate(reader, 8) ?? DateTimeOffset.UtcNow, Puuid = NullableString(reader, 9), SummonerId = NullableInt64(reader, 10), RiotGameName = NullableString(reader, 11), RiotTagLine = NullableString(reader, 12),
        ProfileIconId = NullableInt32(reader, 13), ProfileIconBytes = reader.IsDBNull(14) ? null : (byte[])reader[14], SummonerLevel = NullableInt32(reader, 15), LastSyncedAtUtc = ParseDate(reader, 16), LastMatchPlayedAtUtc = ParseDate(reader, 17), LastMatchId = NullableInt64(reader, 18), MatchHistorySyncedAtUtc = ParseDate(reader, 19), MatchHistoryState = (MatchHistoryState)reader.GetInt32(20),
        RiotPoints = NullableInt64(reader, 21), BlueEssence = NullableInt64(reader, 22)
    };

    private static string? NullableString(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
    private static int? NullableInt32(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetInt32(index);
    private static long? NullableInt64(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetInt64(index);
    private static DateTimeOffset? ParseDate(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : DateTimeOffset.Parse(reader.GetString(index), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private static object Format(DateTimeOffset? value) => value.HasValue
        ? value.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
        : DBNull.Value;

    private SqliteConnection RequireConnection() => _connection ?? throw new InvalidOperationException("The vault is locked.");

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction? transaction, string sql, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InitializeSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, null, SchemaSql, cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync(connection, "riot_points", "INTEGER", cancellationToken).ConfigureAwait(false);
        await EnsureColumnAsync(connection, "blue_essence", "INTEGER", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, null, "UPDATE schema_info SET version=2 WHERE version<2", cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureColumnAsync(SqliteConnection connection, string columnName, string columnType, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('accounts') WHERE name=$name";
        command.Parameters.AddWithValue("$name", columnName);
        var exists = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) > 0;
        if (!exists) await ExecuteAsync(connection, null, $"ALTER TABLE accounts ADD COLUMN {columnName} {columnType}", cancellationToken).ConfigureAwait(false);
    }

    private const string AccountSelect = "SELECT id,login_identifier,password,label,region,notes,roles,created_at,modified_at,puuid,summoner_id,riot_game_name,riot_tag_line,profile_icon_id,profile_icon,summoner_level,last_synced_at,last_match_played_at,last_match_id,match_history_synced_at,match_history_state,riot_points,blue_essence FROM accounts";
    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS schema_info(version INTEGER NOT NULL);
        INSERT INTO schema_info(version) SELECT 1 WHERE NOT EXISTS(SELECT 1 FROM schema_info);
        CREATE TABLE IF NOT EXISTS accounts(
          id TEXT PRIMARY KEY, login_identifier TEXT NOT NULL, password BLOB NOT NULL, label TEXT, region TEXT NOT NULL, notes TEXT, roles INTEGER NOT NULL DEFAULT 0,
          created_at TEXT NOT NULL, modified_at TEXT NOT NULL, puuid TEXT, summoner_id INTEGER, riot_game_name TEXT, riot_tag_line TEXT, profile_icon_id INTEGER, profile_icon BLOB,
          summoner_level INTEGER, last_synced_at TEXT, last_match_played_at TEXT, last_match_id INTEGER, match_history_synced_at TEXT, match_history_state INTEGER NOT NULL DEFAULT 0,
          riot_points INTEGER, blue_essence INTEGER);
        CREATE UNIQUE INDEX IF NOT EXISTS ix_accounts_puuid ON accounts(puuid) WHERE puuid IS NOT NULL;
        CREATE INDEX IF NOT EXISTS ix_accounts_login_region ON accounts(login_identifier COLLATE NOCASE, region);
        CREATE TABLE IF NOT EXISTS ranks(account_id TEXT NOT NULL REFERENCES accounts(id) ON DELETE CASCADE, queue_type TEXT NOT NULL, tier TEXT NOT NULL, division TEXT NOT NULL, league_points INTEGER NOT NULL, wins INTEGER NOT NULL, losses INTEGER NOT NULL, PRIMARY KEY(account_id,queue_type));
        CREATE TABLE IF NOT EXISTS champions(account_id TEXT NOT NULL REFERENCES accounts(id) ON DELETE CASCADE, champion_id INTEGER NOT NULL, name TEXT NOT NULL, PRIMARY KEY(account_id,champion_id));
        CREATE TABLE IF NOT EXISTS skins(account_id TEXT NOT NULL REFERENCES accounts(id) ON DELETE CASCADE, skin_id INTEGER NOT NULL, champion_id INTEGER NOT NULL, name TEXT NOT NULL, PRIMARY KEY(account_id,skin_id));
        """;
}
