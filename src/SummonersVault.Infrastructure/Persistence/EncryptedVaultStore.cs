using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SummonersVault.Application.Abstractions;
using SummonersVault.Application.Accounts;
using SummonersVault.Application.Security;
using SummonersVault.Application.Vault;
using SummonersVault.Core.Models;
using SummonersVault.Infrastructure.Storage;

namespace SummonersVault.Infrastructure.Persistence;

public sealed class EncryptedVaultStore(VaultPaths paths) : IVaultStore, IAccountRepository
{
    internal const string BaselineMigrationId = "20260812203337_InitialCurrentSchema";
    internal const string ChampionProgressionMigrationId = "20260813214021_AddChampionProgression";
    private static readonly TimeSpan MigrationTimeout = TimeSpan.FromSeconds(15);
    private static readonly string[] ChampionProgressionTables = ["champion_masteries", "champion_eternal_summaries", "champion_eternal_sets", "champion_eternals"];
    private static int _sqliteInitialized;
    private int _disposeState;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SqliteConnection? _connection;
    private VaultDbContextFactory? _contextFactory;

    public bool IsOpen => _connection is not null;
    public string DatabasePath => paths.DatabasePath;

    public async Task OpenAsync(ReadOnlyMemory<byte> databaseKey, bool create, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection is not null)
            {
                return;
            }

            paths.EnsureCreated();
            if (!create && !File.Exists(paths.DatabasePath))
            {
                throw new FileNotFoundException("Vault database was not found.", paths.DatabasePath);
            }

            if (Interlocked.Exchange(ref _sqliteInitialized, 1) == 0)
            {
                SQLitePCL.Batteries_V2.Init();
            }

            var connection = new SqliteConnection(CreateConnectionString(databaseKey.Span));
            try
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                await ApplySecurityPragmasAsync(connection, cancellationToken).ConfigureAwait(false);
                var contextFactory = new VaultDbContextFactory(connection);
                await PrepareSchemaAsync(connection, contextFactory, create, cancellationToken).ConfigureAwait(false);
                _connection = connection;
                _contextFactory = contextFactory;
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

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection is null)
            {
                return;
            }

            await _connection.CloseAsync().ConfigureAwait(false);
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
            _contextFactory = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<VaultAccount>> GetAccountsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using VaultDbContext context = CreateContext();
            List<AccountEntity> entities = await AccountReadQuery(context)
                .OrderBy(account => account.Label ?? account.RiotGameName ?? account.LoginIdentifier)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            return [.. entities.Select(MapAccount)];
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<LeagueAccountIdentity>> GetLeagueAccountIdentitiesAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using VaultDbContext context = CreateContext();
            return await context.Accounts
                .AsNoTracking()
                .Select(account => new LeagueAccountIdentity(
                    account.Id,
                    account.LoginIdentifier,
                    account.Label,
                    account.Puuid,
                    account.RiotGameName,
                    account.RiotTagLine))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<VaultAccount?> GetAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using VaultDbContext context = CreateContext();
            AccountEntity? entity = await AccountReadQuery(context)
                .SingleOrDefaultAsync(account => account.Id == accountId, cancellationToken)
                .ConfigureAwait(false);
            return entity is null ? null : MapAccount(entity);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SensitiveBuffer?> GetPasswordAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using VaultDbContext context = CreateContext();
            byte[]? password = await context.Accounts
                .Where(account => account.Id == accountId)
                .Select(account => account.Password)
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            return password is null ? null : new SensitiveBuffer(password);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAccountAsync(AccountSaveRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Account.Username);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Account.Region);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using VaultDbContext context = CreateContext();
            await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await SaveAccountCoreAsync(context, request, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using VaultDbContext context = CreateContext();
            await context.Accounts.Where(account => account.Id == accountId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ApplyLeagueSnapshotAsync(Guid accountId, LeagueSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using VaultDbContext context = CreateTrackingContext();
            await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            AccountEntity entity = await AccountSnapshotTrackedQuery(context).SingleOrDefaultAsync(account => account.Id == accountId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Account no longer exists.");
            try
            {
                ApplySnapshot(entity, snapshot);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(entity.Password);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ApplyChampionProgressionAsync(
        Guid accountId,
        ChampionProgressionSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using VaultDbContext context = CreateTrackingContext();
            await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            AccountEntity entity = await AccountProgressionTrackedQuery(context)
                .SingleOrDefaultAsync(account => account.Id == accountId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException("Account no longer exists.");
            try
            {
                if (!string.Equals(entity.Puuid, snapshot.Puuid, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The account identity changed before champion progression could be saved.");
                }

                ApplyChampionProgression(entity, snapshot);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(entity.Password);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MergeAccountsAsync(IReadOnlyList<AccountImportItem> accounts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using VaultDbContext context = CreateContext();
            await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            foreach (AccountImportItem item in accounts)
            {
                await SaveAccountCoreAsync(context, new AccountSaveRequest(item.Account, item.Password), cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return;
        }

        await CloseAsync().ConfigureAwait(false);
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _gate.Dispose();
    }

    private static async Task SaveAccountCoreAsync(VaultDbContext context, AccountSaveRequest request, CancellationToken cancellationToken)
    {
        VaultAccount account = request.Account;
        AccountEntity? entity = await AccountTrackedQuery(context).SingleOrDefaultAsync(existing => existing.Id == account.Id, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            if (request.Password is null || request.Password.Memory.IsEmpty)
            {
                throw new ArgumentException("A password is required for a new account.", nameof(request));
            }

            entity = new AccountEntity { Id = account.Id, Password = request.Password.Copy() };
            context.Accounts.Add(entity);
        }
        else
        {
            context.Attach(entity);
            if (request.Password is not null)
            {
                entity.Password = request.Password.Copy();
            }

            context.Ranks.RemoveRange(entity.Ranks);
            context.Champions.RemoveRange(entity.Champions);
            context.ChampionMasteries.RemoveRange(entity.ChampionMasteries);
            context.EternalSummaries.RemoveRange(entity.EternalSummaries);
            context.EternalSets.RemoveRange(entity.EternalSets);
            context.Eternals.RemoveRange(entity.Eternals);
            context.Skins.RemoveRange(entity.Skins);
            context.LootItems.RemoveRange(entity.LootItems);
            context.SyncCategories.RemoveRange(entity.SyncCategories);
        }

        try
        {
            CopyAccount(account, entity);
            ReplaceCollections(account, entity);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(entity.Password);
        }
    }

    private VaultDbContext CreateContext() => _contextFactory?.Create() ?? throw new InvalidOperationException("The vault is locked.");

    private VaultDbContext CreateTrackingContext()
    {
        VaultDbContext context = CreateContext();
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
        return context;
    }

    private static IQueryable<AccountEntity> AccountTrackedQuery(VaultDbContext context) => context.Accounts
        .Include(account => account.Ranks)
        .Include(account => account.Champions)
        .Include(account => account.ChampionMasteries)
        .Include(account => account.EternalSummaries)
        .Include(account => account.EternalSets)
        .Include(account => account.Eternals)
        .Include(account => account.Skins)
        .Include(account => account.LootItems)
        .Include(account => account.SyncCategories)
        .AsSplitQuery();

    private static IQueryable<AccountEntity> AccountProgressionTrackedQuery(VaultDbContext context) => context.Accounts
        .Include(account => account.ChampionMasteries)
        .Include(account => account.EternalSummaries)
        .Include(account => account.EternalSets)
        .Include(account => account.Eternals)
        .Include(account => account.SyncCategories)
        .AsSplitQuery();

    private static IQueryable<AccountEntity> AccountSnapshotTrackedQuery(VaultDbContext context) => context.Accounts
        .Include(account => account.Ranks)
        .Include(account => account.Champions)
        .Include(account => account.Skins)
        .Include(account => account.LootItems)
        .Include(account => account.SyncCategories)
        .AsSplitQuery();

    internal static IQueryable<AccountEntity> AccountReadQuery(VaultDbContext context) => context.Accounts
        .AsNoTracking()
        .AsSplitQuery()
        .Select(account => new AccountEntity
        {
            Id = account.Id,
            LoginIdentifier = account.LoginIdentifier,
            Password = Array.Empty<byte>(),
            Label = account.Label,
            Region = account.Region,
            Notes = account.Notes,
            Roles = account.Roles,
            CreatedAtUtc = account.CreatedAtUtc,
            ModifiedAtUtc = account.ModifiedAtUtc,
            Puuid = account.Puuid,
            SummonerId = account.SummonerId,
            RiotGameName = account.RiotGameName,
            RiotTagLine = account.RiotTagLine,
            ProfileIconId = account.ProfileIconId,
            ProfileIcon = account.ProfileIcon,
            SummonerLevel = account.SummonerLevel,
            LastSyncedAtUtc = account.LastSyncedAtUtc,
            LastMatchPlayedAtUtc = account.LastMatchPlayedAtUtc,
            LastMatchId = account.LastMatchId,
            MatchHistorySyncedAtUtc = account.MatchHistorySyncedAtUtc,
            MatchHistoryState = account.MatchHistoryState,
            RiotPoints = account.RiotPoints,
            BlueEssence = account.BlueEssence,
            Ranks = account.Ranks.ToList(),
            Champions = account.Champions.ToList(),
            ChampionMasteries = account.ChampionMasteries.ToList(),
            EternalSummaries = account.EternalSummaries.ToList(),
            EternalSets = account.EternalSets.ToList(),
            Eternals = account.Eternals.ToList(),
            Skins = account.Skins.ToList(),
            LootItems = account.LootItems.ToList(),
            SyncCategories = account.SyncCategories.ToList()
        });

    private string CreateConnectionString(ReadOnlySpan<byte> databaseKey)
    {
        string normalizedPath = Path.GetFullPath(paths.DatabasePath).Replace('\\', '/');
        return new SqliteConnectionStringBuilder
        {
            DataSource = $"file:{normalizedPath}?cipher=sqlcipher&legacy=4",
            Password = $"x'{Convert.ToHexString(databaseKey)}'",
            Pooling = false
        }.ToString();
    }

    private static async Task ApplySecurityPragmasAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA secure_delete=ON; PRAGMA temp_store=MEMORY; PRAGMA synchronous=FULL; PRAGMA journal_mode=DELETE; PRAGMA busy_timeout=15000;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task PrepareSchemaAsync(SqliteConnection connection, VaultDbContextFactory contextFactory, bool create, CancellationToken cancellationToken)
    {
        List<string> tables = await GetTablesAsync(connection, cancellationToken).ConfigureAwait(false);
        if (!tables.Contains("__EFMigrationsHistory", StringComparer.Ordinal))
        {
            if (tables.Count == 0 && create)
            {
                await MigrateAsync(contextFactory, cancellationToken).ConfigureAwait(false);
                return;
            }

            await AdoptPublicSchemaV4Async(connection, tables, cancellationToken).ConfigureAwait(false);
        }

        await MigrateAsync(contextFactory, cancellationToken).ConfigureAwait(false);
    }

    private static async Task MigrateAsync(VaultDbContextFactory contextFactory, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(MigrationTimeout);
        try
        {
            await using VaultDbContext context = contextFactory.Create();
            await context.Database.MigrateAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new VaultUpgradeException("The vault is being upgraded by another process or the upgrade timed out.");
        }
        catch (Exception exception) when (exception is DbUpdateException or SqliteException)
        {
            throw new VaultUpgradeException("The vault database upgrade failed. The vault was left locked.", exception);
        }
    }

    private static async Task AdoptPublicSchemaV4Async(SqliteConnection connection, IReadOnlyCollection<string> tables, CancellationToken cancellationToken)
    {
        if (!tables.Contains("schema_info", StringComparer.Ordinal))
        {
            throw new UnsupportedVaultException("This vault does not contain a supported public schema.");
        }

        await using (SqliteCommand versionCommand = connection.CreateCommand())
        {
            versionCommand.CommandText = "SELECT version FROM schema_info LIMIT 1";
            int version = Convert.ToInt32(await versionCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
            if (version != 4)
            {
                throw new UnsupportedVaultException($"Vault schema version {version} is unsupported. SummonersVault supports public schema version 4 and newer.");
            }
        }

        await ValidateV4SchemaAsync(connection, tables, cancellationToken).ConfigureAwait(false);
        List<string> championColumns = await GetColumnsAsync(connection, "champions", cancellationToken).ConfigureAwait(false);
        bool alreadyHasChampionProgression = championColumns.Contains("alias", StringComparer.Ordinal)
            && championColumns.Contains("variant", StringComparer.Ordinal)
            && ChampionProgressionTables.All(table => tables.Contains(table, StringComparer.Ordinal));
        await using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            CREATE TABLE "__EFMigrationsHistory" (
                "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                "ProductVersion" TEXT NOT NULL
            );
            INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion") VALUES ($migration, '10.0.10');
            DROP TABLE schema_info;
            """;
        command.Parameters.AddWithValue("$migration", BaselineMigrationId);
        if (alreadyHasChampionProgression)
        {
            command.CommandText = command.CommandText.Replace(
                "DROP TABLE schema_info;",
                "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ($progressionMigration, '10.0.10'); DROP TABLE schema_info;",
                StringComparison.Ordinal);
            command.Parameters.AddWithValue("$progressionMigration", ChampionProgressionMigrationId);
        }
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ValidateV4SchemaAsync(SqliteConnection connection, IReadOnlyCollection<string> tables, CancellationToken cancellationToken)
    {
        var required = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["accounts"] = [
                "id", "login_identifier", "password", "label", "region", "notes", "roles", "created_at", "modified_at",
                "puuid", "summoner_id", "riot_game_name", "riot_tag_line", "profile_icon_id", "profile_icon", "summoner_level",
                "last_synced_at", "last_match_played_at", "last_match_id", "match_history_synced_at", "match_history_state",
                "riot_points", "blue_essence"
            ],
            ["ranks"] = [
                "account_id", "queue_type", "tier", "division", "league_points", "wins", "losses", "is_provisional",
                "provisional_games_remaining", "rated_tier", "rated_rating"
            ],
            ["champions"] = ["account_id", "champion_id", "name", "base_splash_path", "square_portrait_path"],
            ["skins"] = ["account_id", "skin_id", "champion_id", "name", "splash_path", "tile_path"],
            ["loot_items"] = [
                "account_id", "loot_id", "loot_name", "type", "display_category", "localized_name", "localized_description",
                "count", "rarity", "reference_id", "asset_path", "splash_path", "tile_path", "expires_at", "disenchant_value",
                "upgrade_essence_value"
            ],
            ["account_sync_categories"] = ["account_id", "category", "state", "last_attempt_at", "last_success_at"]
        };

        foreach ((string? table, string[]? columns) in required)
        {
            if (!tables.Contains(table, StringComparer.Ordinal))
            {
                throw new UnsupportedVaultException($"The vault schema is missing the required {table} table.");
            }

            List<string> actualColumns = await GetColumnsAsync(connection, table, cancellationToken).ConfigureAwait(false);
            if (columns.Any(column => !actualColumns.Contains(column, StringComparer.Ordinal)))
            {
                throw new UnsupportedVaultException($"The vault schema contains an incomplete {table} table.");
            }
        }
    }

    private static async Task<List<string>> GetTablesAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var result = new List<string>();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }

    private static async Task<List<string>> GetColumnsAsync(SqliteConnection connection, string table, CancellationToken cancellationToken)
    {
        var result = new List<string>();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT name FROM pragma_table_info('{table}')";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }

    private static VaultAccount MapAccount(AccountEntity entity)
    {
        var account = new VaultAccount
        {
            Id = entity.Id,
            Username = entity.LoginIdentifier,
            Label = entity.Label,
            Region = LeagueRegion.Normalize(entity.Region),
            Notes = entity.Notes,
            Roles = (AccountRole)entity.Roles,
            CreatedAtUtc = entity.CreatedAtUtc,
            ModifiedAtUtc = entity.ModifiedAtUtc,
            Puuid = entity.Puuid,
            SummonerId = entity.SummonerId,
            RiotGameName = entity.RiotGameName,
            RiotTagLine = entity.RiotTagLine,
            ProfileIconId = entity.ProfileIconId,
            ProfileIconBytes = entity.ProfileIcon,
            SummonerLevel = entity.SummonerLevel,
            LastSyncedAtUtc = entity.LastSyncedAtUtc,
            LastMatchPlayedAtUtc = entity.LastMatchPlayedAtUtc,
            LastMatchId = entity.LastMatchId,
            MatchHistorySyncedAtUtc = entity.MatchHistorySyncedAtUtc,
            MatchHistoryState = (MatchHistoryState)entity.MatchHistoryState,
            RiotPoints = entity.RiotPoints,
            BlueEssence = entity.BlueEssence
        };
        account.Ranks.AddRange(entity.Ranks.Select(rank => new RankSnapshot(rank.QueueType, rank.Tier, rank.Division, rank.LeaguePoints, rank.Wins, rank.Losses, rank.IsProvisional, rank.ProvisionalGamesRemaining, rank.RatedTier, rank.RatedRating)));
        account.Champions.AddRange(entity.Champions.Select(champion => new OwnedChampion(champion.ChampionId, champion.Name, champion.BaseSplashPath, champion.SquarePortraitPath, champion.Alias, (ChampionVariant)champion.Variant)));
        account.ChampionMasteries.AddRange(entity.ChampionMasteries.Select(MapMastery));
        account.EternalSummaries.AddRange(entity.EternalSummaries.Select(summary => new ChampionEternalSummary(summary.ChampionId, summary.MilestonesPassed, summary.StonesAvailable, summary.StonesIlluminated, summary.StonesOwned)));
        account.EternalSets.AddRange(entity.EternalSets.Select(set => new ChampionEternalSet(set.ChampionId, set.SetId, set.Name, set.MilestonesPassed, set.StonesAvailable, set.StonesIlluminated, set.StonesOwned)));
        account.Eternals.AddRange(entity.Eternals.Select(MapEternal));
        account.Skins.AddRange(OwnedSkinRules.Normalize(entity.Skins.Select(skin => new OwnedSkin(skin.SkinId, skin.ChampionId, skin.Name, skin.SplashPath, skin.TilePath))));
        account.LootItems.AddRange(entity.LootItems.Select(loot => new CraftingLootItem(loot.LootId, loot.LootName, loot.Type, loot.DisplayCategory, loot.LocalizedName, loot.LocalizedDescription, loot.Count, loot.Rarity, loot.ReferenceId, loot.AssetPath, loot.SplashPath, loot.TilePath, loot.ExpiresAtUtc, loot.DisenchantValue, loot.UpgradeEssenceValue)));
        account.SyncCategories.AddRange(entity.SyncCategories.Select(status => new SnapshotCategoryStatus((SnapshotCategory)status.Category, (SnapshotState)status.State, status.LastAttemptAtUtc, status.LastSuccessAtUtc)));
        return account;
    }

    private static void CopyAccount(VaultAccount source, AccountEntity target)
    {
        target.LoginIdentifier = source.Username;
        target.Label = source.Label;
        target.Region = LeagueRegion.Normalize(source.Region);
        target.Notes = source.Notes;
        target.Roles = (int)source.Roles;
        target.CreatedAtUtc = source.CreatedAtUtc;
        target.ModifiedAtUtc = source.ModifiedAtUtc;
        target.Puuid = source.Puuid;
        target.SummonerId = source.SummonerId;
        target.RiotGameName = source.RiotGameName;
        target.RiotTagLine = source.RiotTagLine;
        target.ProfileIconId = source.ProfileIconId;
        target.ProfileIcon = source.ProfileIconBytes;
        target.SummonerLevel = source.SummonerLevel;
        target.LastSyncedAtUtc = source.LastSyncedAtUtc;
        target.LastMatchPlayedAtUtc = source.LastMatchPlayedAtUtc;
        target.LastMatchId = source.LastMatchId;
        target.MatchHistorySyncedAtUtc = source.MatchHistorySyncedAtUtc;
        target.MatchHistoryState = (int)source.MatchHistoryState;
        target.RiotPoints = source.RiotPoints;
        target.BlueEssence = source.BlueEssence;
    }

    private static void ReplaceCollections(VaultAccount source, AccountEntity target)
    {
        target.Ranks = [.. source.Ranks.Select(rank => new RankEntity { AccountId = source.Id, QueueType = rank.QueueType, Tier = rank.Tier, Division = rank.Division, LeaguePoints = rank.LeaguePoints, Wins = rank.Wins, Losses = rank.Losses, IsProvisional = rank.IsProvisional, ProvisionalGamesRemaining = rank.ProvisionalGamesRemaining, RatedTier = rank.RatedTier, RatedRating = rank.RatedRating })];
        target.Champions = [.. source.Champions.Select(champion => MapChampion(source.Id, champion))];
        target.ChampionMasteries = [.. source.ChampionMasteries.Select(mastery => MapMastery(source.Id, mastery))];
        target.EternalSummaries = [.. source.EternalSummaries.Select(summary => MapEternalSummary(source.Id, summary))];
        target.EternalSets = [.. source.EternalSets.Select(set => MapEternalSet(source.Id, set))];
        target.Eternals = [.. source.Eternals.Select(eternal => MapEternal(source.Id, eternal))];
        target.Skins = [.. OwnedSkinRules.Normalize(source.Skins).Select(skin => new SkinEntity { AccountId = source.Id, SkinId = skin.SkinId, ChampionId = skin.ChampionId, Name = skin.Name, SplashPath = skin.SplashAssetPath, TilePath = skin.TileAssetPath })];
        target.LootItems = [.. source.LootItems.Select(loot => new LootItemEntity { AccountId = source.Id, LootId = loot.LootId, LootName = loot.LootName, Type = loot.Type, DisplayCategory = loot.DisplayCategory, LocalizedName = loot.LocalizedName, LocalizedDescription = loot.LocalizedDescription, Count = loot.Count, Rarity = loot.Rarity, ReferenceId = loot.ReferenceId, AssetPath = loot.AssetPath, SplashPath = loot.SplashAssetPath, TilePath = loot.TileAssetPath, ExpiresAtUtc = loot.ExpiresAtUtc, DisenchantValue = loot.DisenchantValue, UpgradeEssenceValue = loot.UpgradeEssenceValue })];
        target.SyncCategories = [.. source.SyncCategories.Select(status => new SyncCategoryEntity { AccountId = source.Id, Category = (int)status.Category, State = (int)status.State, LastAttemptAtUtc = status.LastAttemptAtUtc, LastSuccessAtUtc = status.LastSuccessAtUtc })];
    }

    private static void ApplySnapshot(AccountEntity entity, LeagueSnapshot snapshot)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        entity.Puuid = snapshot.Puuid;
        entity.SummonerId = snapshot.SummonerId;
        entity.RiotGameName = snapshot.RiotGameName;
        entity.RiotTagLine = snapshot.RiotTagLine;
        entity.Region = LeagueRegion.Normalize(snapshot.Region);
        entity.ProfileIconId = snapshot.ProfileIconId;
        entity.ProfileIcon = snapshot.ProfileIconBytes ?? entity.ProfileIcon;
        entity.SummonerLevel = snapshot.SummonerLevel;
        entity.RiotPoints = snapshot.Wallet?.RiotPoints ?? entity.RiotPoints;
        entity.BlueEssence = snapshot.Wallet?.BlueEssence ?? entity.BlueEssence;
        entity.LastSyncedAtUtc = now;
        entity.ModifiedAtUtc = now;
        if (snapshot.Ranks is not null)
        {
            entity.Ranks = [.. snapshot.Ranks.Select(rank => new RankEntity { AccountId = entity.Id, QueueType = rank.QueueType, Tier = rank.Tier, Division = rank.Division, LeaguePoints = rank.LeaguePoints, Wins = rank.Wins, Losses = rank.Losses, IsProvisional = rank.IsProvisional, ProvisionalGamesRemaining = rank.ProvisionalGamesRemaining, RatedTier = rank.RatedTier, RatedRating = rank.RatedRating })];
        }

        if (snapshot.Champions is not null)
        {
            entity.Champions = [.. snapshot.Champions.Select(champion => MapChampion(entity.Id, champion))];
        }

        if (snapshot.Skins is not null)
        {
            entity.Skins = [.. OwnedSkinRules.Normalize(snapshot.Skins).Select(skin => new SkinEntity { AccountId = entity.Id, SkinId = skin.SkinId, ChampionId = skin.ChampionId, Name = skin.Name, SplashPath = skin.SplashAssetPath, TilePath = skin.TileAssetPath })];
        }

        if (snapshot.CraftingLoot is not null)
        {
            entity.LootItems = [.. snapshot.CraftingLoot.Select(loot => new LootItemEntity { AccountId = entity.Id, LootId = loot.LootId, LootName = loot.LootName, Type = loot.Type, DisplayCategory = loot.DisplayCategory, LocalizedName = loot.LocalizedName, LocalizedDescription = loot.LocalizedDescription, Count = loot.Count, Rarity = loot.Rarity, ReferenceId = loot.ReferenceId, AssetPath = loot.AssetPath, SplashPath = loot.SplashAssetPath, TilePath = loot.TileAssetPath, ExpiresAtUtc = loot.ExpiresAtUtc, DisenchantValue = loot.DisenchantValue, UpgradeEssenceValue = loot.UpgradeEssenceValue })];
        }

        UpdateCategory(entity, SnapshotCategory.Ranked, snapshot.Ranks is not null, now);
        UpdateCategory(entity, SnapshotCategory.Wallet, snapshot.Wallet is { RiotPoints: not null, BlueEssence: not null }, now);
        UpdateCategory(entity, SnapshotCategory.Champions, snapshot.Champions is not null, now);
        UpdateCategory(entity, SnapshotCategory.Skins, snapshot.Skins is not null, now);
        UpdateCategory(entity, SnapshotCategory.Crafting, snapshot.CraftingLoot is not null, now);
        if (snapshot.ChampionMasteries is not null || snapshot.ChampionEternals is not null)
        {
            ApplyChampionProgression(entity, new ChampionProgressionSnapshot
            {
                Puuid = snapshot.Puuid,
                ChampionMasteries = snapshot.ChampionMasteries,
                ChampionEternals = snapshot.ChampionEternals
            });
        }

        if (snapshot.Match.Succeeded)
        {
            entity.MatchHistorySyncedAtUtc = now;
            if (snapshot.Match.HasMatch && snapshot.Match.PlayedAtUtc is { } played && (!entity.LastMatchPlayedAtUtc.HasValue || played >= entity.LastMatchPlayedAtUtc.Value))
            {
                entity.LastMatchPlayedAtUtc = played;
                entity.LastMatchId = snapshot.Match.MatchId;
                entity.MatchHistoryState = (int)MatchHistoryState.Known;
            }
            else if (!entity.LastMatchPlayedAtUtc.HasValue)
            {
                entity.MatchHistoryState = (int)MatchHistoryState.NeverPlayed;
            }
        }
        else if (entity.LastMatchPlayedAtUtc.HasValue)
        {
            entity.MatchHistoryState = (int)MatchHistoryState.Stale;
        }
    }

    private static void ApplyChampionProgression(AccountEntity entity, ChampionProgressionSnapshot snapshot)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        entity.ModifiedAtUtc = now;
        if (snapshot.ChampionMasteries is not null)
        {
            entity.ChampionMasteries = [.. snapshot.ChampionMasteries.Select(mastery => MapMastery(entity.Id, mastery))];
        }

        if (snapshot.ChampionEternals is { } eternals)
        {
            entity.EternalSummaries = [.. eternals.Summaries.Select(summary => MapEternalSummary(entity.Id, summary))];
            HashSet<int> clearChampionIds =
            [
                .. eternals.Summaries.Where(summary => summary.StonesOwned == 0).Select(summary => summary.ChampionId),
                .. eternals.SuccessfullyLoadedChampionIds
            ];
            entity.EternalSets.RemoveAll(set => clearChampionIds.Contains(set.ChampionId));
            entity.Eternals.RemoveAll(eternal => clearChampionIds.Contains(eternal.ChampionId));
            entity.EternalSets.AddRange(eternals.Sets.Select(set => MapEternalSet(entity.Id, set)));
            entity.Eternals.AddRange(eternals.Eternals.Select(eternal => MapEternal(entity.Id, eternal)));
        }

        UpdateCategory(entity, SnapshotCategory.Mastery, snapshot.ChampionMasteries is not null, now);
        UpdateEternalsCategory(entity, snapshot.ChampionEternals, now);
    }

    private static ChampionEntity MapChampion(Guid accountId, OwnedChampion champion) => new()
    {
        AccountId = accountId,
        ChampionId = champion.ChampionId,
        Name = champion.Name,
        BaseSplashPath = champion.BaseSplashAssetPath,
        SquarePortraitPath = champion.SquarePortraitAssetPath,
        Alias = champion.Alias,
        Variant = (int)champion.Variant
    };

    private static ChampionMasteryEntity MapMastery(Guid accountId, ChampionMastery mastery) => new()
    {
        AccountId = accountId,
        ChampionId = mastery.ChampionId,
        Level = mastery.Level,
        Points = mastery.Points,
        PointsSinceLastLevel = mastery.PointsSinceLastLevel,
        PointsUntilNextLevel = mastery.PointsUntilNextLevel,
        SeasonMilestone = mastery.SeasonMilestone,
        HighestGrade = mastery.HighestGrade,
        LastPlayAtUtc = mastery.LastPlayAtUtc,
        MarksRequiredForNextLevel = mastery.MarksRequiredForNextLevel,
        MilestoneGradesJson = JsonSerializer.Serialize(mastery.MilestoneGrades),
        TokensEarned = mastery.TokensEarned
    };

    private static ChampionMastery MapMastery(ChampionMasteryEntity mastery) => new(
        mastery.ChampionId, mastery.Level, mastery.Points, mastery.PointsSinceLastLevel,
        mastery.PointsUntilNextLevel, mastery.SeasonMilestone, mastery.HighestGrade, mastery.LastPlayAtUtc,
        mastery.MarksRequiredForNextLevel, DeserializeGrades(mastery.MilestoneGradesJson), mastery.TokensEarned);

    private static string[] DeserializeGrades(string value)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(value) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static EternalSummaryEntity MapEternalSummary(Guid accountId, ChampionEternalSummary summary) => new()
    {
        AccountId = accountId,
        ChampionId = summary.ChampionId,
        MilestonesPassed = summary.MilestonesPassed,
        StonesAvailable = summary.StonesAvailable,
        StonesIlluminated = summary.StonesIlluminated,
        StonesOwned = summary.StonesOwned
    };

    private static EternalSetEntity MapEternalSet(Guid accountId, ChampionEternalSet set) => new()
    {
        AccountId = accountId,
        ChampionId = set.ChampionId,
        SetId = set.SetId,
        Name = set.Name,
        MilestonesPassed = set.MilestonesPassed,
        StonesAvailable = set.StonesAvailable,
        StonesIlluminated = set.StonesIlluminated,
        StonesOwned = set.StonesOwned
    };

    private static EternalEntity MapEternal(Guid accountId, ChampionEternal eternal) => new()
    {
        AccountId = accountId,
        ChampionId = eternal.ChampionId,
        SetId = eternal.SetId,
        StatstoneId = eternal.StatstoneId,
        Name = eternal.Name,
        Description = eternal.Description,
        Category = eternal.Category,
        Value = eternal.Value,
        FormattedValue = eternal.FormattedValue,
        MilestoneLevel = eternal.MilestoneLevel,
        FormattedMilestoneLevel = eternal.FormattedMilestoneLevel,
        NextMilestone = eternal.NextMilestone,
        PersonalBest = eternal.PersonalBest,
        FormattedPersonalBest = eternal.FormattedPersonalBest,
        IsComplete = eternal.IsComplete,
        IsEpic = eternal.IsEpic,
        IsFeatured = eternal.IsFeatured,
        IsRetired = eternal.IsRetired,
        ImageAssetPath = eternal.ImageAssetPath
    };

    private static ChampionEternal MapEternal(EternalEntity eternal) => new(
        eternal.ChampionId, eternal.SetId, eternal.StatstoneId, eternal.Name, eternal.Description, eternal.Category,
        eternal.Value, eternal.FormattedValue, eternal.MilestoneLevel, eternal.FormattedMilestoneLevel,
        eternal.NextMilestone, eternal.PersonalBest, eternal.FormattedPersonalBest, eternal.IsComplete,
        eternal.IsEpic, eternal.IsFeatured, eternal.IsRetired, eternal.ImageAssetPath);

    private static void UpdateCategory(AccountEntity account, SnapshotCategory category, bool succeeded, DateTimeOffset attemptedAt)
    {
        SyncCategoryEntity? previous = account.SyncCategories.FirstOrDefault(status => status.Category == (int)category);
        account.SyncCategories.RemoveAll(status => status.Category == (int)category);
        account.SyncCategories.Add(new SyncCategoryEntity
        {
            AccountId = account.Id,
            Category = (int)category,
            State = (int)(succeeded ? SnapshotState.Current : previous?.LastSuccessAtUtc is null ? SnapshotState.Unknown : SnapshotState.Stale),
            LastAttemptAtUtc = attemptedAt,
            LastSuccessAtUtc = succeeded ? attemptedAt : previous?.LastSuccessAtUtc
        });
    }

    private static void UpdateEternalsCategory(AccountEntity account, ChampionEternalsSnapshot? snapshot, DateTimeOffset attemptedAt)
    {
        if (snapshot is null || snapshot.IsComplete)
        {
            UpdateCategory(account, SnapshotCategory.Eternals, snapshot?.IsComplete == true, attemptedAt);
            return;
        }

        SyncCategoryEntity? previous = account.SyncCategories.FirstOrDefault(status => status.Category == (int)SnapshotCategory.Eternals);
        account.SyncCategories.RemoveAll(status => status.Category == (int)SnapshotCategory.Eternals);
        account.SyncCategories.Add(new SyncCategoryEntity
        {
            AccountId = account.Id,
            Category = (int)SnapshotCategory.Eternals,
            State = (int)SnapshotState.Stale,
            LastAttemptAtUtc = attemptedAt,
            LastSuccessAtUtc = previous?.LastSuccessAtUtc
        });
    }
}
