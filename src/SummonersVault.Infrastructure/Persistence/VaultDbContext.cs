using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace SummonersVault.Infrastructure.Persistence;

internal sealed class VaultDbContext(DbContextOptions<VaultDbContext> options) : DbContext(options)
{
    public DbSet<AccountEntity> Accounts => Set<AccountEntity>();
    public DbSet<RankEntity> Ranks => Set<RankEntity>();
    public DbSet<ChampionEntity> Champions => Set<ChampionEntity>();
    public DbSet<SkinEntity> Skins => Set<SkinEntity>();
    public DbSet<LootItemEntity> LootItems => Set<LootItemEntity>();
    public DbSet<SyncCategoryEntity> SyncCategories => Set<SyncCategoryEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var dateTimeOffsetConverter = new ValueConverter<DateTimeOffset, string>(
            value => value.ToUniversalTime().ToString("O"),
            value => DateTimeOffset.Parse(value, null, System.Globalization.DateTimeStyles.RoundtripKind));
        var nullableDateTimeOffsetConverter = new ValueConverter<DateTimeOffset?, string?>(
            value => value.HasValue ? value.Value.ToUniversalTime().ToString("O") : null,
            value => value == null ? null : DateTimeOffset.Parse(value, null, System.Globalization.DateTimeStyles.RoundtripKind));

        EntityTypeBuilder<AccountEntity> account = modelBuilder.Entity<AccountEntity>();
        account.ToTable("accounts");
        account.HasKey(entity => entity.Id);
        account.Property(entity => entity.Id).HasColumnName("id").HasConversion<string>();
        account.Property(entity => entity.LoginIdentifier).HasColumnName("login_identifier").IsRequired();
        account.Property(entity => entity.Password).HasColumnName("password").IsRequired();
        account.Property(entity => entity.Label).HasColumnName("label");
        account.Property(entity => entity.Region).HasColumnName("region").IsRequired();
        account.Property(entity => entity.Notes).HasColumnName("notes");
        account.Property(entity => entity.Roles).HasColumnName("roles");
        account.Property(entity => entity.CreatedAtUtc).HasColumnName("created_at").HasConversion(dateTimeOffsetConverter);
        account.Property(entity => entity.ModifiedAtUtc).HasColumnName("modified_at").HasConversion(dateTimeOffsetConverter);
        account.Property(entity => entity.Puuid).HasColumnName("puuid");
        account.Property(entity => entity.SummonerId).HasColumnName("summoner_id");
        account.Property(entity => entity.RiotGameName).HasColumnName("riot_game_name");
        account.Property(entity => entity.RiotTagLine).HasColumnName("riot_tag_line");
        account.Property(entity => entity.ProfileIconId).HasColumnName("profile_icon_id");
        account.Property(entity => entity.ProfileIcon).HasColumnName("profile_icon");
        account.Property(entity => entity.SummonerLevel).HasColumnName("summoner_level");
        account.Property(entity => entity.LastSyncedAtUtc).HasColumnName("last_synced_at").HasConversion(nullableDateTimeOffsetConverter);
        account.Property(entity => entity.LastMatchPlayedAtUtc).HasColumnName("last_match_played_at").HasConversion(nullableDateTimeOffsetConverter);
        account.Property(entity => entity.LastMatchId).HasColumnName("last_match_id");
        account.Property(entity => entity.MatchHistorySyncedAtUtc).HasColumnName("match_history_synced_at").HasConversion(nullableDateTimeOffsetConverter);
        account.Property(entity => entity.MatchHistoryState).HasColumnName("match_history_state");
        account.Property(entity => entity.RiotPoints).HasColumnName("riot_points");
        account.Property(entity => entity.BlueEssence).HasColumnName("blue_essence");
        account.HasIndex(entity => entity.Puuid).IsUnique().HasFilter("puuid IS NOT NULL").HasDatabaseName("ix_accounts_puuid");
        account.HasIndex(entity => new { entity.LoginIdentifier, entity.Region }).HasDatabaseName("ix_accounts_login_region");

        ConfigureRank(modelBuilder);
        ConfigureChampion(modelBuilder);
        ConfigureSkin(modelBuilder);
        ConfigureLoot(modelBuilder, nullableDateTimeOffsetConverter);
        ConfigureSyncCategory(modelBuilder, nullableDateTimeOffsetConverter);
    }

    private static void ConfigureRank(ModelBuilder modelBuilder)
    {
        EntityTypeBuilder<RankEntity> entity = modelBuilder.Entity<RankEntity>();
        entity.ToTable("ranks");
        entity.HasKey(rank => new { rank.AccountId, rank.QueueType });
        entity.Property(rank => rank.AccountId).HasColumnName("account_id").HasConversion<string>();
        entity.Property(rank => rank.QueueType).HasColumnName("queue_type");
        entity.Property(rank => rank.Tier).HasColumnName("tier");
        entity.Property(rank => rank.Division).HasColumnName("division");
        entity.Property(rank => rank.LeaguePoints).HasColumnName("league_points");
        entity.Property(rank => rank.Wins).HasColumnName("wins");
        entity.Property(rank => rank.Losses).HasColumnName("losses");
        entity.Property(rank => rank.IsProvisional).HasColumnName("is_provisional").HasConversion<int>();
        entity.Property(rank => rank.ProvisionalGamesRemaining).HasColumnName("provisional_games_remaining");
        entity.Property(rank => rank.RatedTier).HasColumnName("rated_tier");
        entity.Property(rank => rank.RatedRating).HasColumnName("rated_rating");
        entity.HasOne(rank => rank.Account).WithMany(account => account.Ranks).HasForeignKey(rank => rank.AccountId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureChampion(ModelBuilder modelBuilder)
    {
        EntityTypeBuilder<ChampionEntity> entity = modelBuilder.Entity<ChampionEntity>();
        entity.ToTable("champions");
        entity.HasKey(champion => new { champion.AccountId, champion.ChampionId });
        entity.Property(champion => champion.AccountId).HasColumnName("account_id").HasConversion<string>();
        entity.Property(champion => champion.ChampionId).HasColumnName("champion_id");
        entity.Property(champion => champion.Name).HasColumnName("name");
        entity.Property(champion => champion.BaseSplashPath).HasColumnName("base_splash_path");
        entity.Property(champion => champion.SquarePortraitPath).HasColumnName("square_portrait_path");
        entity.HasOne(champion => champion.Account).WithMany(account => account.Champions).HasForeignKey(champion => champion.AccountId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureSkin(ModelBuilder modelBuilder)
    {
        EntityTypeBuilder<SkinEntity> entity = modelBuilder.Entity<SkinEntity>();
        entity.ToTable("skins");
        entity.HasKey(skin => new { skin.AccountId, skin.SkinId });
        entity.Property(skin => skin.AccountId).HasColumnName("account_id").HasConversion<string>();
        entity.Property(skin => skin.SkinId).HasColumnName("skin_id");
        entity.Property(skin => skin.ChampionId).HasColumnName("champion_id");
        entity.Property(skin => skin.Name).HasColumnName("name");
        entity.Property(skin => skin.SplashPath).HasColumnName("splash_path");
        entity.Property(skin => skin.TilePath).HasColumnName("tile_path");
        entity.HasOne(skin => skin.Account).WithMany(account => account.Skins).HasForeignKey(skin => skin.AccountId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureLoot(ModelBuilder modelBuilder, ValueConverter<DateTimeOffset?, string?> dateConverter)
    {
        EntityTypeBuilder<LootItemEntity> entity = modelBuilder.Entity<LootItemEntity>();
        entity.ToTable("loot_items");
        entity.HasKey(loot => new { loot.AccountId, loot.LootId });
        entity.Property(loot => loot.AccountId).HasColumnName("account_id").HasConversion<string>();
        entity.Property(loot => loot.LootId).HasColumnName("loot_id");
        entity.Property(loot => loot.LootName).HasColumnName("loot_name");
        entity.Property(loot => loot.Type).HasColumnName("type");
        entity.Property(loot => loot.DisplayCategory).HasColumnName("display_category");
        entity.Property(loot => loot.LocalizedName).HasColumnName("localized_name");
        entity.Property(loot => loot.LocalizedDescription).HasColumnName("localized_description");
        entity.Property(loot => loot.Count).HasColumnName("count");
        entity.Property(loot => loot.Rarity).HasColumnName("rarity");
        entity.Property(loot => loot.ReferenceId).HasColumnName("reference_id");
        entity.Property(loot => loot.AssetPath).HasColumnName("asset_path");
        entity.Property(loot => loot.SplashPath).HasColumnName("splash_path");
        entity.Property(loot => loot.TilePath).HasColumnName("tile_path");
        entity.Property(loot => loot.ExpiresAtUtc).HasColumnName("expires_at").HasConversion(dateConverter);
        entity.Property(loot => loot.DisenchantValue).HasColumnName("disenchant_value");
        entity.Property(loot => loot.UpgradeEssenceValue).HasColumnName("upgrade_essence_value");
        entity.HasIndex(loot => new { loot.AccountId, loot.Type }).HasDatabaseName("ix_loot_account_type");
        entity.HasIndex(loot => new { loot.AccountId, loot.DisplayCategory }).HasDatabaseName("ix_loot_account_category");
        entity.HasIndex(loot => new { loot.AccountId, loot.LocalizedName }).HasDatabaseName("ix_loot_account_name");
        entity.HasOne(loot => loot.Account).WithMany(account => account.LootItems).HasForeignKey(loot => loot.AccountId).OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureSyncCategory(ModelBuilder modelBuilder, ValueConverter<DateTimeOffset?, string?> dateConverter)
    {
        EntityTypeBuilder<SyncCategoryEntity> entity = modelBuilder.Entity<SyncCategoryEntity>();
        entity.ToTable("account_sync_categories");
        entity.HasKey(status => new { status.AccountId, status.Category });
        entity.Property(status => status.AccountId).HasColumnName("account_id").HasConversion<string>();
        entity.Property(status => status.Category).HasColumnName("category");
        entity.Property(status => status.State).HasColumnName("state");
        entity.Property(status => status.LastAttemptAtUtc).HasColumnName("last_attempt_at").HasConversion(dateConverter);
        entity.Property(status => status.LastSuccessAtUtc).HasColumnName("last_success_at").HasConversion(dateConverter);
        entity.HasOne(status => status.Account).WithMany(account => account.SyncCategories).HasForeignKey(status => status.AccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class VaultDbContextFactory(SqliteConnection connection, IInterceptor? interceptor = null)
{
    public VaultDbContext Create()
    {
        DbContextOptionsBuilder<VaultDbContext> optionsBuilder = new DbContextOptionsBuilder<VaultDbContext>()
            .UseSqlite(connection, sqliteOptions =>
                sqliteOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            .ConfigureWarnings(warnings =>
                warnings.Throw(RelationalEventId.MultipleCollectionIncludeWarning))
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        if (interceptor is not null)
        {
            optionsBuilder.AddInterceptors(interceptor);
        }

        return new VaultDbContext(optionsBuilder.Options);
    }
}
