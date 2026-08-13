using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SummonersVault.Infrastructure.Persistence;
using Xunit;

namespace SummonersVault.Tests;

public sealed class PersistenceQueryTests
{
    [Fact]
    public async Task AccountReadQuery_LoadsSiblingCollectionsUsingSeparateCommands()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var commandCounter = new ReaderCommandCounter();
        var contextFactory = new VaultDbContextFactory(connection, commandCounter);

        await SeedAccountGraphAsync(contextFactory);

        commandCounter.Reset();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using VaultDbContext readContext = contextFactory.Create();

        AccountEntity account = Assert.Single(await EncryptedVaultStore.AccountReadQuery(readContext)
            .ToListAsync(timeout.Token));

        Assert.Equal(2, account.Ranks.Count);
        Assert.Equal(2, account.Champions.Count);
        Assert.Equal(2, account.Skins.Count);
        Assert.Equal(2, account.LootItems.Count);
        Assert.Equal(2, account.SyncCategories.Count);
        Assert.Equal(6, commandCounter.Count);
    }

    [Fact]
    public async Task VaultDbContext_DefaultsSiblingCollectionQueriesToSplitQueries()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var commandCounter = new ReaderCommandCounter();
        var contextFactory = new VaultDbContextFactory(connection, commandCounter);

        await SeedAccountGraphAsync(contextFactory);

        commandCounter.Reset();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using VaultDbContext readContext = contextFactory.Create();

        List<AccountEntity> accounts = await readContext.Accounts
            .Include(account => account.Ranks)
            .Include(account => account.Champions)
            .Include(account => account.Skins)
            .Include(account => account.LootItems)
            .Include(account => account.SyncCategories)
            .ToListAsync(timeout.Token);

        Assert.Single(accounts);
        Assert.Equal(6, commandCounter.Count);
    }

    private static async Task SeedAccountGraphAsync(VaultDbContextFactory contextFactory)
    {
        await using VaultDbContext setupContext = contextFactory.Create();
        await setupContext.Database.EnsureCreatedAsync();
        setupContext.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
        setupContext.Accounts.Add(CreateAccountGraph());
        await setupContext.SaveChangesAsync();
    }

    private static AccountEntity CreateAccountGraph()
    {
        Guid accountId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new AccountEntity
        {
            Id = accountId,
            LoginIdentifier = "split-query-test",
            Password = [1],
            Region = "EUW",
            CreatedAtUtc = now,
            ModifiedAtUtc = now,
            Ranks =
            [
                new() { AccountId = accountId, QueueType = "RANKED_SOLO_5x5" },
                new() { AccountId = accountId, QueueType = "RANKED_FLEX_SR" }
            ],
            Champions =
            [
                new() { AccountId = accountId, ChampionId = 1, Name = "Champion 1" },
                new() { AccountId = accountId, ChampionId = 2, Name = "Champion 2" }
            ],
            Skins =
            [
                new() { AccountId = accountId, SkinId = 1, ChampionId = 1, Name = "Skin 1" },
                new() { AccountId = accountId, SkinId = 2, ChampionId = 2, Name = "Skin 2" }
            ],
            LootItems =
            [
                new() { AccountId = accountId, LootId = "loot-1", LootName = "Loot 1" },
                new() { AccountId = accountId, LootId = "loot-2", LootName = "Loot 2" }
            ],
            SyncCategories =
            [
                new() { AccountId = accountId, Category = 1 },
                new() { AccountId = accountId, Category = 2 }
            ]
        };
    }

    private sealed class ReaderCommandCounter : DbCommandInterceptor
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Reset() => Interlocked.Exchange(ref _count, 0);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
