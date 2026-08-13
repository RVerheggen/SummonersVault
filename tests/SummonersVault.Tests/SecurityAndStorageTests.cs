using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using SummonersVault.Core.Models;
using SummonersVault.Application.Abstractions;
using SummonersVault.Application.Accounts;
using SummonersVault.Application.Security;
using SummonersVault.Application.Vault;
using SummonersVault.Core.Services;
using SummonersVault.Infrastructure.League;
using SummonersVault.Infrastructure.Backup;
using SummonersVault.Infrastructure.Artwork;
using SummonersVault.Infrastructure.Security;
using SummonersVault.Infrastructure.Storage;
using SummonersVault.Infrastructure.Persistence;
using Xunit;
using System.Diagnostics;

namespace SummonersVault.Tests;

public sealed class SecurityAndStorageTests
{
    [Fact]
    public void MasterPassword_AcceptsEightCharacters_AndRejectsSeven()
    {
        VaultKeyEnvelope.ValidateMasterPassword(Encoding.UTF8.GetBytes("12345678"));
        Assert.Throws<ArgumentException>(() => VaultKeyEnvelope.ValidateMasterPassword(Encoding.UTF8.GetBytes("1234567")));
    }

    [Fact]
    public void Envelope_RejectsWrongPassword_AndDetectsCorruption()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        VaultMetadata metadata = VaultKeyEnvelope.Create(Guid.NewGuid(), "correct horse"u8, key);
        Assert.False(VaultKeyEnvelope.TryUnwrap(metadata, "wrong password"u8, out _));
        VaultMetadata corrupt = metadata with { KeyEnvelope = metadata.KeyEnvelope with { TagBase64 = Convert.ToBase64String(new byte[16]) } };
        Assert.False(VaultKeyEnvelope.TryUnwrap(corrupt, "correct horse"u8, out _));
        Assert.True(VaultKeyEnvelope.TryUnwrap(metadata, "correct horse"u8, out byte[]? opened));
        Assert.Equal(key, opened);
        CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(opened);
    }

    [Fact]
    public void SensitiveBuffer_ClearsOwnedBytesWhenDisposed()
    {
        byte[] bytes = "temporary-secret"u8.ToArray();
        var buffer = new SensitiveBuffer(bytes);

        buffer.Dispose();

        Assert.All(bytes, value => Assert.Equal(0, value));
        Assert.Throws<ObjectDisposedException>(() => buffer.Copy());
    }

    [Fact]
    public async Task Session_RewrapsDatabaseKey_WithoutChangingVaultData()
    {
        string root = Path.Combine(Path.GetTempPath(), "SummonersVaultTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new VaultPaths(root); await using var repository = new EncryptedVaultStore(paths); await using var session = new VaultSession(paths, repository);
            await session.CreateAsync("old password"u8.ToArray(), TestContext.Current.CancellationToken);
            using (var password = new SensitiveBuffer("kept-secret"u8.ToArray()))
            {
                await repository.SaveAccountAsync(new(new VaultAccount { Username = "rewrap-user", Region = "EUW1" }, password), TestContext.Current.CancellationToken);
            }
            await session.ChangeMasterPasswordAsync("old password"u8.ToArray(), "new password"u8.ToArray(), TestContext.Current.CancellationToken); await session.LockAsync(TestContext.Current.CancellationToken);
            Assert.False(await session.UnlockAsync("old password"u8.ToArray(), TestContext.Current.CancellationToken)); Assert.True(await session.UnlockAsync("new password"u8.ToArray(), TestContext.Current.CancellationToken));
            Assert.Equal("rewrap-user", Assert.Single(await repository.GetAccountsAsync(TestContext.Current.CancellationToken)).Username);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Search_MatchesOwnedContentFacets_AndUsesSoloRank()
    {
        var account = new VaultAccount { Username = "learn-top", Label = "Top practice", Region = "EUW1", Roles = AccountRole.Top, Notes = "weakside" };
        account.Ranks.Add(new("RANKED_FLEX_SR", "SILVER", "I", 50, 1, 1)); account.Ranks.Add(new("RANKED_SOLO_5x5", "GOLD", "IV", 20, 2, 1));
        account.Champions.Add(new(103, "Ahri")); account.Skins.Add(new(103000, 103, "Classic Ahri")); account.Skins.Add(new(103001, 103, "Dynasty Ahri"));
        Assert.Equal("GOLD", account.CardRank?.Tier);
        Assert.Single(AccountSearch.Apply([account], "dynasty weakside", AccountSort.Name));
        Assert.Single(AccountSearch.Apply([account], null, AccountSort.Name, new(Region: "EUW1", Queue: "RANKED_SOLO_5x5", Rank: "gold iv", Roles: AccountRole.Top, Champion: "ahri", Skin: "dynasty")));
        Assert.Single(AccountSearch.Apply([account], null, AccountSort.Name, new(Region: "EUW")));
        Assert.Single(AccountSearch.Apply([account], null, AccountSort.Name, new(Rank: " gold iv ", Champion: " ahri ", Skin: " dynasty ")));
        Assert.Empty(AccountSearch.Apply([account], null, AccountSort.Name, new(Queue: "RANKED_FLEX_SR", Rank: "gold")));
        Assert.Single(AccountSearch.Apply([account], null, AccountSort.Name, new(Roles: AccountRole.Top | AccountRole.Jungle)));
        Assert.Empty(AccountSearch.Apply([account], null, AccountSort.Name, new(Skin: "classic")));
        Assert.Empty(AccountSearch.Apply([account], null, AccountSort.Name, new(Region: "NA")));
    }

    [Theory]
    [InlineData("EUW1", "EUW")]
    [InlineData("euw", "EUW")]
    [InlineData(" EUN1 ", "EUNE")]
    [InlineData("NA1", "NA")]
    [InlineData("LA1", "LAN")]
    [InlineData("LA2", "LAS")]
    [InlineData("OC1", "OCE")]
    [InlineData("KR1", "KR")]
    [InlineData("TEST9", "TEST")]
    public void LeagueRegion_NormalizesPlatformRoutesForDisplay(string input, string expected) =>
        Assert.Equal(expected, LeagueRegion.Normalize(input));

    [Fact]
    public void LeagueIdentityRules_AllowFirstLinkAndRejectADifferentLinkedProfile()
    {
        var account = new VaultAccount { Username = "player", Region = "EUW1" };
        Assert.True(LeagueIdentityRules.MatchesLinkedAccount(account, "signed-in-puuid"));

        account.Puuid = "linked-puuid";
        Assert.True(LeagueIdentityRules.MatchesLinkedAccount(account, "linked-puuid"));
        Assert.False(LeagueIdentityRules.MatchesLinkedAccount(account, "different-puuid"));
    }

    [Theory]
    [InlineData(103000, 103, "Ahri", false)]
    [InlineData(103999, 103, "Classic Ahri", false)]
    [InlineData(103998, 103, "Original Ahri", false)]
    [InlineData(103997, 103, "Default Ahri", false)]
    [InlineData(103001, 103, "Dynasty Ahri", true)]
    public void OwnedSkinRules_ExcludeBaseAndClassicSkins(int skinId, int championId, string name, bool expected) =>
        Assert.Equal(expected, OwnedSkinRules.IsCounted(new(skinId, championId, name)));

    [Fact]
    public void OwnedSkinRules_NormalizeCanonicalizesAlternateNamespaceAndDeduplicates()
    {
        IReadOnlyList<OwnedSkin> normalized = OwnedSkinRules.Normalize([
            new(19016, 19, "PROJECT: Warwick"),
            new(60019016, 60019, "PROJECT: Warwick")
        ]);

        OwnedSkin skin = Assert.Single(normalized);
        Assert.Equal(19016, skin.SkinId);
        Assert.Equal(19, skin.ChampionId);
        Assert.Equal("PROJECT: Warwick", skin.Name);
    }

    [Fact]
    public void OwnedSkinRules_CanonicalizeDoesNotChangeUnrecognizedHighIds()
    {
        var skin = new OwnedSkin(70019016, 70019, "Future skin");
        Assert.Equal(skin, OwnedSkinRules.Canonicalize(skin));
    }

    [Theory]
    [InlineData("LeagueClient:1234:2999:secret:https", true)]
    [InlineData("LeagueClient:bad:2999:secret:https", false)]
    [InlineData("LeagueClient:1234:70000:secret:https", false)]
    [InlineData("", false)]
    public void LockfileParser_ValidatesInput(string value, bool expected) => Assert.Equal(expected, LeagueLockfile.TryParse(value, out _));

    [Fact]
    public void LeagueInstallationDiscovery_MapsRiotAndLeagueFoldersAndLaunchArguments()
    {
        string riotGames = Path.Combine(Path.GetTempPath(), "Riot Games");
        string leagueDirectory = Path.Combine(riotGames, "League of Legends");
        string riotClientExecutable = Path.Combine(riotGames, "Riot Client", "RiotClientServices.exe");

        Assert.Contains(leagueDirectory, LeagueInstallationLocator.GetLeagueDirectories(riotClientExecutable, riotGames), StringComparer.OrdinalIgnoreCase);
        Assert.Contains(riotClientExecutable, LeagueInstallationLocator.GetRiotClientExecutables(leagueDirectory, riotGames), StringComparer.OrdinalIgnoreCase);

        ProcessStartInfo startInfo = LeagueInstallationLocator.CreateLaunchStartInfo(riotClientExecutable);
        Assert.Equal(riotClientExecutable, startInfo.FileName);
        Assert.Equal(["--launch-product=league_of_legends", "--launch-patchline=live"], startInfo.ArgumentList);
        Assert.True(Path.IsPathRooted(LeagueInstallationLocator.DefaultRiotGamesDirectory));
    }

    [Fact]
    public void LeagueInstallationDiscovery_UsesLeagueClientUxAndLimitsCertificateBypassToHttpsLoopback()
    {
        string riotGames = Path.Combine(Path.GetTempPath(), "Riot Games");
        string discoveredDirectory = Path.Combine(Path.GetTempPath(), "Custom League");
        string clientUx = Path.Combine(discoveredDirectory, "LeagueClientUx.exe");

        Assert.Contains(discoveredDirectory, LeagueInstallationLocator.GetLeagueDirectories(null, riotGames, [clientUx]), StringComparer.OrdinalIgnoreCase);
        Assert.True(LeagueClientGateway.IsLeagueLoopbackRequest(new Uri("https://127.0.0.1:12345/")));
        Assert.False(LeagueClientGateway.IsLeagueLoopbackRequest(new Uri("https://localhost:12345/")));
        Assert.False(LeagueClientGateway.IsLeagueLoopbackRequest(new Uri("http://127.0.0.1:12345/")));
        Assert.False(LeagueClientGateway.IsLeagueLoopbackRequest(new Uri("https://example.com/")));
    }

    [Theory]
    [InlineData("[]", true)]
    [InlineData("[{}]", true)]
    [InlineData("{}", false)]
    public void InventoryReadiness_AcceptsSuccessfulEmptyCategories(string json, bool expected)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(expected, LeagueClientGateway.IsInventoryPayloadReady(document.RootElement));
    }

    [Theory]
    [InlineData("{\"rp\":1350,\"ip\":42000}", 1350L, 42000L)]
    [InlineData("{\"rp\":0,\"ip\":0}", 0L, 0L)]
    [InlineData("{\"rp\":1350}", 1350L, null)]
    [InlineData("{\"RP\":\"250\",\"blueEssence\":\"7000\"}", 250L, 7000L)]
    [InlineData("{\"RP\":250,\"lol_blue_essence\":7000}", 250L, 7000L)]
    [InlineData("[]", null, null)]
    public void WalletParser_MapsLegacyIpFieldToBlueEssence(string json, long? expectedRp, long? expectedBlueEssence)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        LeagueWalletSnapshot? wallet = LeagueClientGateway.ParseWallet(document.RootElement);
        Assert.Equal(expectedRp, wallet?.RiotPoints);
        Assert.Equal(expectedBlueEssence, wallet?.BlueEssence);
    }

    [Theory]
    [InlineData("250", "RP", 250L)]
    [InlineData("\"7000\"", "IP", 7000L)]
    [InlineData("{\"RP\":250}", "RP", 250L)]
    [InlineData("{\"lol_blue_essence\":7000}", "lol_blue_essence", 7000L)]
    [InlineData("{\"balance\":7000}", "IP", 7000L)]
    [InlineData("{\"quantity\":7000}", "IP", 7000L)]
    [InlineData("{}", "RP", null)]
    public void WalletCurrencyParser_AcceptsInventoryEndpointShapes(string json, string currencyType, long? expected)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(expected, LeagueClientGateway.ParseWalletCurrency(document.RootElement, currencyType));
    }

    [Fact]
    public void LeagueSnapshot_RequiresBothInventoryCategoriesToBeComplete()
    {
        LeagueSnapshot snapshot = Snapshot(MatchSnapshotResult.Failed);
        Assert.False(snapshot.HasCompleteInventory);
        Assert.False(snapshot.HasCompleteSyncData);
        var completeInventory = new LeagueSnapshot
        {
            Puuid = "puuid",
            RiotGameName = "Player",
            RiotTagLine = "EUW",
            Region = "EUW1",
            Champions = [],
            Skins = []
        };
        Assert.True(completeInventory.HasCompleteInventory);
        Assert.False(completeInventory.HasCompleteSyncData);
        Assert.True(new LeagueSnapshot
        {
            Puuid = "puuid",
            RiotGameName = "Player",
            RiotTagLine = "EUW",
            Region = "EUW1",
            Champions = [],
            Skins = [],
            Ranks = [],
            CraftingLoot = [],
            Wallet = new(0, 0)
        }.HasCompleteSyncData);
    }

    [Fact]
    public void LootParser_ExcludesZeroCounts_AndPreservesGenericMetadata()
    {
        using var document = System.Text.Json.JsonDocument.Parse("""[{"lootId":"CHAMPION_RENTAL_1","lootName":"Champion shard","type":"CHAMPION_RENTAL","displayCategories":"Champion shards","localizedName":"Annie champion shard","count":2,"rarity":"Epic","refId":"1","tilePath":"/lol-game-data/assets/v1/champion-tiles/1/1000.jpg","disenchantValue":90,"upgradeEssenceValue":450},{"lootId":"empty","count":0}]""");
        CraftingLootItem loot = Assert.Single(LeagueClientGateway.ParseCraftingLoot(document.RootElement));
        Assert.Equal("Annie champion shard", loot.LocalizedName);
        Assert.Equal(2, loot.Count);
        Assert.Equal(90, loot.DisenchantValue);
        Assert.Equal("Champion shards", loot.DisplayCategory);
    }

    [Fact]
    public void LootParser_NormalizesLcuCategories_AndBlankNames()
    {
        using var document = System.Text.Json.JsonDocument.Parse("""[{"lootId":"skin-1","lootName":"PROJECT_WARWICK","type":"SKIN","displayCategories":"SKIN","localizedName":"","count":1,"refId":700161}]""");
        CraftingLootItem loot = Assert.Single(LeagueClientGateway.ParseCraftingLoot(document.RootElement));
        Assert.Equal("Skin shards", loot.DisplayCategory);
        Assert.Equal("PROJECT_WARWICK", loot.LocalizedName);
        Assert.Equal("700161", loot.ReferenceId);
    }

    [Theory]
    [InlineData("CURRENCY_champion", "Blue Essence")]
    [InlineData("CURRENCY_cosmetic", "Orange Essence")]
    public void LootParser_UsesFriendlyEssenceNames(string lootName, string expected)
    {
        using var document = System.Text.Json.JsonDocument.Parse($$"""[{"lootId":"{{lootName}}","lootName":"{{lootName}}","type":"CURRENCY","count":10}]""");
        Assert.Equal(expected, Assert.Single(LeagueClientGateway.ParseCraftingLoot(document.RootElement)).LocalizedName);
    }

    [Theory]
    [InlineData("/lol-game-data/assets/v1/champion-splashes/1/1000.jpg", true)]
    [InlineData("/lol-game-data/assets/../secret", false)]
    [InlineData("https://example.com/image.png", false)]
    public void ArtworkMapping_AllowsOnlyDocumentedPublicPaths(string path, bool expected)
    {
        Assert.Equal(expected, ArtworkCacheService.TryMapCommunityDragon(path, out Uri? uri));
        if (expected)
        {
            Assert.Equal("raw.communitydragon.org", uri.Host);
        }
    }

    [Fact]
    public async Task SchemaV4_PreservesRichSnapshots_AndMarksFailedCategoriesStale()
    {
        string root = Path.Combine(Path.GetTempPath(), "SummonersVaultTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new VaultPaths(root); await using var repository = new EncryptedVaultStore(paths);
            await repository.OpenAsync(RandomNumberGenerator.GetBytes(32), create: true, TestContext.Current.CancellationToken);
            var account = new VaultAccount { Username = "rich", Region = "EUW" };
            using (var password = new SensitiveBuffer("secret"u8.ToArray()))
            {
                await repository.SaveAccountAsync(new(account, password), TestContext.Current.CancellationToken);
            }
            await repository.ApplyLeagueSnapshotAsync(account.Id, new LeagueSnapshot
            {
                Puuid = "rich-puuid",
                RiotGameName = "Player",
                RiotTagLine = "EUW",
                Region = "EUW1",
                Ranks = [new("RANKED_SOLO_5x5", "GOLD", "II", 44, 20, 10, true, 2)],
                Champions = [new(1, "Annie", "/lol-game-data/assets/a.jpg", "/lol-game-data/assets/b.jpg")],
                Skins = [new(1001, 1, "Goth Annie", "/lol-game-data/assets/c.jpg", "/lol-game-data/assets/d.jpg")],
                CraftingLoot = [new("loot", "loot", "MATERIAL", "Materials", "Key", null, 3, "Rare", null, null, null, "/lol-game-data/assets/key.png", null, 10, 20)],
                Wallet = new(100, 200)
            }, TestContext.Current.CancellationToken);
            await repository.ApplyLeagueSnapshotAsync(account.Id, Snapshot(MatchSnapshotResult.Failed), TestContext.Current.CancellationToken);
            VaultAccount? loaded = await repository.GetAccountAsync(account.Id, TestContext.Current.CancellationToken);
            Assert.Equal("/lol-game-data/assets/a.jpg", Assert.Single(loaded!.Champions).BaseSplashAssetPath);
            Assert.True(Assert.Single(loaded.Ranks).IsProvisional);
            Assert.Equal(3, Assert.Single(loaded.LootItems).Count);
            Assert.All(loaded.SyncCategories, state => Assert.Equal(SnapshotState.Stale, state.State));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public async Task LeagueLockfileReader_AllowsTheClientToKeepItsWriteHandleOpen()
    {
        string path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "LeagueClient:1234:2999:secret:https", TestContext.Current.CancellationToken);
            await using var clientHandle = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read);
            await Assert.ThrowsAsync<IOException>(() => File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            Assert.Equal("LeagueClient:1234:2999:secret:https", await LeagueClientGateway.ReadLockfileAsync(path, TestContext.Current.CancellationToken));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("{\"games\":{\"games\":[{\"gameId\":7,\"gameCreation\":1785969000000}]}}", true, 7)]
    [InlineData("{\"games\":[{\"gameId\":8,\"gameCreationDate\":\"2026-08-05T22:30:00Z\"}]}", true, 8)]
    [InlineData("{\"games\":[]}", false, null)]
    [InlineData("{\"games\":[{\"gameCreation\":\"bad\"}]}", false, null)]
    public void MatchHistoryParser_HandlesNumericTextEmptyAndMalformed(string json, bool hasMatch, int? matchId)
    {
        MatchSnapshotResult result = LeagueMatchHistoryParser.Parse(json);
        Assert.Equal(hasMatch, result.HasMatch); Assert.Equal((long?)matchId, result.MatchId);
        if (json.Contains("[]", StringComparison.Ordinal))
        {
            Assert.True(result.Succeeded);
        }
    }

    [Fact]
    public async Task EncryptedRepository_HidesSecrets_AndPreservesMonotonicMatchHistory()
    {
        string root = Path.Combine(Path.GetTempPath(), "SummonersVaultTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new VaultPaths(root); byte[] key = RandomNumberGenerator.GetBytes(32);
            await using var repository = new EncryptedVaultStore(paths);
            await repository.OpenAsync(key, create: true, TestContext.Current.CancellationToken);
            var account = new VaultAccount { Username = "plaintext-login-marker", Region = "EUW1", Roles = AccountRole.Mid | AccountRole.Support };
            account.Champions.Add(new(1, "Annie")); account.Skins.Add(new(1000, 1, "Annie")); account.Skins.Add(new(1001, 1, "Goth Annie"));
            using (var password = new SensitiveBuffer(Encoding.UTF8.GetBytes("plaintext-password-marker")))
            {
                await repository.SaveAccountAsync(new(account, password), TestContext.Current.CancellationToken);
            }
            var newest = new DateTimeOffset(2026, 8, 5, 22, 30, 0, TimeSpan.Zero);
            await repository.ApplyLeagueSnapshotAsync(account.Id, Snapshot(MatchSnapshotResult.Known(newest, 42), new(975, 123456)), TestContext.Current.CancellationToken);
            await repository.ApplyLeagueSnapshotAsync(account.Id, Snapshot(MatchSnapshotResult.Known(newest.AddDays(-1), 41)), TestContext.Current.CancellationToken);
            VaultAccount? loaded = await repository.GetAccountAsync(account.Id, TestContext.Current.CancellationToken);
            Assert.Equal(newest, loaded?.LastMatchPlayedAtUtc); Assert.Equal(42, loaded?.LastMatchId);
            await repository.ApplyLeagueSnapshotAsync(account.Id, Snapshot(MatchSnapshotResult.Failed), TestContext.Current.CancellationToken);
            loaded = await repository.GetAccountAsync(account.Id, TestContext.Current.CancellationToken);
            Assert.Equal(MatchHistoryState.Stale, loaded?.MatchHistoryState); Assert.Equal(newest, loaded?.LastMatchPlayedAtUtc);
            Assert.Equal("Goth Annie", Assert.Single(loaded!.Skins).Name);
            Assert.Equal(975, loaded.RiotPoints); Assert.Equal(123456, loaded.BlueEssence);
            await repository.CloseAsync(TestContext.Current.CancellationToken);
            byte[] bytes = await File.ReadAllBytesAsync(paths.DatabasePath, TestContext.Current.CancellationToken); string text = Encoding.UTF8.GetString(bytes);
            Assert.DoesNotContain("plaintext-login-marker", text, StringComparison.Ordinal); Assert.DoesNotContain("plaintext-password-marker", text, StringComparison.Ordinal);
            await Assert.ThrowsAnyAsync<Exception>(async () => { await using var wrong = new EncryptedVaultStore(paths); await wrong.OpenAsync(RandomNumberGenerator.GetBytes(32), create: false, TestContext.Current.CancellationToken); });
            CryptographicOperations.ZeroMemory(key);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task EncryptedRepository_ReplacesCollectionsAndRetainsPasswordWhenEditing()
    {
        string root = Path.Combine(Path.GetTempPath(), "SummonersVaultTests", Guid.NewGuid().ToString("N"));
        byte[] key = RandomNumberGenerator.GetBytes(32);
        try
        {
            var paths = new VaultPaths(root);
            await using var repository = new EncryptedVaultStore(paths);
            await repository.OpenAsync(key, create: true, TestContext.Current.CancellationToken);
            var account = new VaultAccount { Username = "edit-user", Region = "EUW" };
            account.Champions.Add(new(1, "Annie"));
            using (var password = new SensitiveBuffer("retained-secret"u8.ToArray()))
            {
                await repository.SaveAccountAsync(new(account, password), TestContext.Current.CancellationToken);
            }

            account.Champions.Clear();
            account.Champions.Add(new(2, "Olaf"));
            await repository.SaveAccountAsync(new(account, null), TestContext.Current.CancellationToken);

            VaultAccount loaded = Assert.Single(await repository.GetAccountsAsync(TestContext.Current.CancellationToken));
            Assert.Equal("Olaf", Assert.Single(loaded.Champions).Name);
            using SensitiveBuffer? retainedPassword = await repository.GetPasswordAsync(account.Id, TestContext.Current.CancellationToken);
            Assert.NotNull(retainedPassword);
            Assert.Equal("retained-secret", Encoding.UTF8.GetString(retainedPassword.Memory.Span));

            await repository.DeleteAccountAsync(account.Id, TestContext.Current.CancellationToken);
            Assert.Empty(await repository.GetAccountsAsync(TestContext.Current.CancellationToken));
            Assert.Null(await repository.GetPasswordAsync(account.Id, TestContext.Current.CancellationToken));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task EncryptedRepository_RejectsUnsupportedPreReleaseSchemaWithoutModifyingIt()
    {
        string root = Path.Combine(Path.GetTempPath(), "SummonersVaultTests", Guid.NewGuid().ToString("N"));
        var paths = new VaultPaths(root);
        byte[] key = RandomNumberGenerator.GetBytes(32);
        try
        {
            await using (var initial = new EncryptedVaultStore(paths))
            {
                await initial.OpenAsync(key, create: true, TestContext.Current.CancellationToken);
                await initial.CloseAsync(TestContext.Current.CancellationToken);
            }

            string normalizedPath = Path.GetFullPath(paths.DatabasePath).Replace('\\', '/');
            string connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = $"file:{normalizedPath}?cipher=sqlcipher&legacy=4",
                Password = $"x'{Convert.ToHexString(key)}'",
                Pooling = false
            }.ToString();
            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync(TestContext.Current.CancellationToken);
                await using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "DROP TABLE \"__EFMigrationsHistory\"; CREATE TABLE schema_info(version INTEGER NOT NULL); INSERT INTO schema_info(version) VALUES(3);";
                await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }

            await using var unsupported = new EncryptedVaultStore(paths);
            UnsupportedVaultException exception = await Assert.ThrowsAsync<UnsupportedVaultException>(() => unsupported.OpenAsync(key, create: false, TestContext.Current.CancellationToken));
            Assert.Contains("version 3", exception.Message, StringComparison.Ordinal);

            await using var verification = new SqliteConnection(connectionString);
            await verification.OpenAsync(TestContext.Current.CancellationToken);
            await using SqliteCommand verificationCommand = verification.CreateCommand();
            verificationCommand.CommandText = "SELECT version FROM schema_info";
            Assert.Equal(3L, await verificationCommand.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task EncryptedRepository_AdoptsPublicSchemaV4AndPreservesData()
    {
        string root = Path.Combine(Path.GetTempPath(), "SummonersVaultTests", Guid.NewGuid().ToString("N"));
        var paths = new VaultPaths(root);
        byte[] key = RandomNumberGenerator.GetBytes(32);
        var account = new VaultAccount { Username = "public-v4-user", Region = "EUW", RiotPoints = 42, BlueEssence = 1234 };
        account.Champions.Add(new(1, "Annie"));

        try
        {
            await using (var initial = new EncryptedVaultStore(paths))
            {
                await initial.OpenAsync(key, create: true, TestContext.Current.CancellationToken);
                using var initialPassword = new SensitiveBuffer("public-secret"u8.ToArray());
                await initial.SaveAccountAsync(new(account, initialPassword), TestContext.Current.CancellationToken);
                await initial.CloseAsync(TestContext.Current.CancellationToken);
            }

            string normalizedPath = Path.GetFullPath(paths.DatabasePath).Replace('\\', '/');
            string connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = $"file:{normalizedPath}?cipher=sqlcipher&legacy=4",
                Password = $"x'{Convert.ToHexString(key)}'",
                Pooling = false
            }.ToString();
            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync(TestContext.Current.CancellationToken);
                await using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "DROP TABLE \"__EFMigrationsHistory\"; CREATE TABLE schema_info(version INTEGER NOT NULL); INSERT INTO schema_info(version) VALUES(4);";
                await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }

            await using var adopted = new EncryptedVaultStore(paths);
            await adopted.OpenAsync(key, create: false, TestContext.Current.CancellationToken);
            VaultAccount loaded = Assert.Single(await adopted.GetAccountsAsync(TestContext.Current.CancellationToken));
            Assert.Equal("public-v4-user", loaded.Username);
            Assert.Equal(42, loaded.RiotPoints);
            Assert.Equal("Annie", Assert.Single(loaded.Champions).Name);

            using SensitiveBuffer? password = await adopted.GetPasswordAsync(loaded.Id, TestContext.Current.CancellationToken);
            Assert.NotNull(password);
            Assert.Equal("public-secret", Encoding.UTF8.GetString(password.Memory.Span));
            await adopted.CloseAsync(TestContext.Current.CancellationToken);

            await using var verification = new SqliteConnection(connectionString);
            await verification.OpenAsync(TestContext.Current.CancellationToken);
            await using SqliteCommand verificationCommand = verification.CreateCommand();
            verificationCommand.CommandText = "SELECT COUNT(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\"=$migration; SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='schema_info';";
            verificationCommand.Parameters.AddWithValue("$migration", EncryptedVaultStore.BaselineMigrationId);
            await using SqliteDataReader reader = await verificationCommand.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
            Assert.Equal(1L, reader.GetInt64(0));
            Assert.True(await reader.NextResultAsync(TestContext.Current.CancellationToken));
            Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
            Assert.Equal(0L, reader.GetInt64(0));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Backup_ImportsAcrossDifferentMasterPasswords_AndPreservesMatchMetadata()
    {
        string root = Path.Combine(Path.GetTempPath(), "SummonersVaultTests", Guid.NewGuid().ToString("N"));
        string sourceRoot = Path.Combine(root, "source"); string targetRoot = Path.Combine(root, "target"); string archive = Path.Combine(root, "portable.svault");
        Directory.CreateDirectory(root);
        try
        {
            var sourcePaths = new VaultPaths(sourceRoot); await using var sourceRepository = new EncryptedVaultStore(sourcePaths); await using var sourceSession = new VaultSession(sourcePaths, sourceRepository);
            await sourceSession.CreateAsync("source password"u8.ToArray(), TestContext.Current.CancellationToken);
            var account = new VaultAccount { Username = "backup-user", Region = "NA1", LastMatchPlayedAtUtc = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero), LastMatchId = 999, MatchHistoryState = MatchHistoryState.Stale, RiotPoints = 500, BlueEssence = 25000 };
            using (var password = new SensitiveBuffer("backup-secret"u8.ToArray()))
            {
                await sourceRepository.SaveAccountAsync(new(account, password), TestContext.Current.CancellationToken);
            }
            await new VaultBackupService(sourcePaths, sourceSession, sourceSession, sourceRepository).ExportAsync(archive, TestContext.Current.CancellationToken);

            var targetPaths = new VaultPaths(targetRoot); await using var targetRepository = new EncryptedVaultStore(targetPaths); await using var targetSession = new VaultSession(targetPaths, targetRepository);
            await targetSession.CreateAsync("target password"u8.ToArray(), TestContext.Current.CancellationToken);
            var targetBackup = new VaultBackupService(targetPaths, targetSession, targetSession, targetRepository);
            await using (BackupImportPreview preview = await targetBackup.PreviewImportAsync(archive, "source password"u8.ToArray(), TestContext.Current.CancellationToken))
            {
                await targetBackup.ImportAsync(preview, new Dictionary<Guid, BackupConflictChoice>(), TestContext.Current.CancellationToken);
            }

            VaultAccount imported = Assert.Single(await targetRepository.GetAccountsAsync(TestContext.Current.CancellationToken));
            Assert.Equal(account.LastMatchPlayedAtUtc, imported.LastMatchPlayedAtUtc); Assert.Equal(999, imported.LastMatchId); Assert.Equal(MatchHistoryState.Stale, imported.MatchHistoryState);
            Assert.Equal(500, imported.RiotPoints); Assert.Equal(25000, imported.BlueEssence);
            using SensitiveBuffer? importedPassword = await targetRepository.GetPasswordAsync(imported.Id, TestContext.Current.CancellationToken);
            Assert.NotNull(importedPassword);
            Assert.Equal("backup-secret", Encoding.UTF8.GetString(importedPassword.Memory.Span));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static LeagueSnapshot Snapshot(MatchSnapshotResult match, LeagueWalletSnapshot? wallet = null) => new() { Puuid = "puuid", RiotGameName = "Player", RiotTagLine = "EUW", Region = "EUW1", Match = match, Wallet = wallet };
}
