using System.Security.Cryptography;
using System.Text;
using SummonersVault.Core.Models;
using SummonersVault.Core.Abstractions;
using SummonersVault.Core.Services;
using SummonersVault.Infrastructure.League;
using SummonersVault.Infrastructure.Backup;
using SummonersVault.Infrastructure.Security;
using SummonersVault.Infrastructure.Storage;
using Xunit;

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
        var key = RandomNumberGenerator.GetBytes(32);
        var metadata = VaultKeyEnvelope.Create(Guid.NewGuid(), "correct horse"u8, key);
        Assert.False(VaultKeyEnvelope.TryUnwrap(metadata, "wrong password"u8, out _));
        var corrupt = metadata with { KeyEnvelope = metadata.KeyEnvelope with { TagBase64 = Convert.ToBase64String(new byte[16]) } };
        Assert.False(VaultKeyEnvelope.TryUnwrap(corrupt, "correct horse"u8, out _));
        Assert.True(VaultKeyEnvelope.TryUnwrap(metadata, "correct horse"u8, out var opened));
        Assert.Equal(key, opened);
        CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(opened);
    }

    [Fact]
    public async Task Session_RewrapsDatabaseKey_WithoutChangingVaultData()
    {
        var root = Path.Combine(Path.GetTempPath(), "SummonersVaultTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new VaultPaths(root); var repository = new EncryptedSqliteVaultRepository(paths); await using var session = new VaultSession(paths, repository);
            await session.CreateAsync("old password"u8.ToArray());
            await repository.SaveAccountAsync(new VaultAccount { LoginIdentifier = "rewrap-user", PasswordUtf8 = "kept-secret"u8.ToArray(), Region = "EUW1" });
            await session.ChangeMasterPasswordAsync("old password"u8.ToArray(), "new password"u8.ToArray()); await session.LockAsync();
            Assert.False(await session.UnlockAsync("old password"u8.ToArray())); Assert.True(await session.UnlockAsync("new password"u8.ToArray()));
            Assert.Equal("rewrap-user", Assert.Single(await repository.GetAccountsAsync()).LoginIdentifier);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Search_MatchesOwnedContentFacets_AndUsesSoloRank()
    {
        var account = new VaultAccount { LoginIdentifier = "learn-top", Label = "Top practice", Region = "EUW1", Roles = AccountRole.Top, Notes = "weakside" };
        account.Ranks.Add(new("RANKED_FLEX_SR", "SILVER", "I", 50, 1, 1)); account.Ranks.Add(new("RANKED_SOLO_5x5", "GOLD", "IV", 20, 2, 1));
        account.Champions.Add(new(103, "Ahri")); account.Skins.Add(new(103000, 103, "Classic Ahri")); account.Skins.Add(new(103001, 103, "Dynasty Ahri"));
        Assert.Equal("GOLD", account.CardRank?.Tier);
        Assert.Single(AccountSearch.Apply([account], "dynasty weakside", AccountSort.Name));
        Assert.Single(AccountSearch.Apply([account], null, AccountSort.Name, new(Region: "EUW1", Queue: "RANKED_SOLO_5x5", Rank: "gold iv", Roles: AccountRole.Top, Champion: "ahri", Skin: "dynasty")));
        Assert.Empty(AccountSearch.Apply([account], null, AccountSort.Name, new(Queue: "RANKED_FLEX_SR", Rank: "gold")));
        Assert.Single(AccountSearch.Apply([account], null, AccountSort.Name, new(Roles: AccountRole.Top | AccountRole.Jungle)));
        Assert.Empty(AccountSearch.Apply([account], null, AccountSort.Name, new(Skin: "classic")));
        Assert.Empty(AccountSearch.Apply([account], null, AccountSort.Name, new(Region: "NA")));
    }

    [Fact]
    public void LeagueIdentityRules_AllowFirstLinkAndRejectADifferentLinkedProfile()
    {
        var account = new VaultAccount { LoginIdentifier = "player", Region = "EUW1" };
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
        var normalized = OwnedSkinRules.Normalize([
            new(19016, 19, "PROJECT: Warwick"),
            new(60019016, 60019, "PROJECT: Warwick")
        ]);

        var skin = Assert.Single(normalized);
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
        var riotGames = Path.Combine(Path.GetTempPath(), "Riot Games");
        var leagueDirectory = Path.Combine(riotGames, "League of Legends");
        var riotClientExecutable = Path.Combine(riotGames, "Riot Client", "RiotClientServices.exe");

        Assert.Contains(leagueDirectory, LeagueInstallationLocator.GetLeagueDirectories(riotClientExecutable, riotGames), StringComparer.OrdinalIgnoreCase);
        Assert.Contains(riotClientExecutable, LeagueInstallationLocator.GetRiotClientExecutables(leagueDirectory, riotGames), StringComparer.OrdinalIgnoreCase);

        var startInfo = LeagueInstallationLocator.CreateLaunchStartInfo(riotClientExecutable);
        Assert.Equal(riotClientExecutable, startInfo.FileName);
        Assert.Equal(["--launch-product=league_of_legends", "--launch-patchline=live"], startInfo.ArgumentList);
        Assert.True(Path.IsPathRooted(LeagueInstallationLocator.DefaultRiotGamesDirectory));
    }

    [Fact]
    public void LeagueInstallationDiscovery_UsesLeagueClientUxAndLimitsCertificateBypassToHttpsLoopback()
    {
        var riotGames = Path.Combine(Path.GetTempPath(), "Riot Games");
        var discoveredDirectory = Path.Combine(Path.GetTempPath(), "Custom League");
        var clientUx = Path.Combine(discoveredDirectory, "LeagueClientUx.exe");

        Assert.Contains(discoveredDirectory, LeagueInstallationLocator.GetLeagueDirectories(null, riotGames, [clientUx]), StringComparer.OrdinalIgnoreCase);
        Assert.True(LeagueClientGateway.IsLeagueLoopbackRequest(new Uri("https://127.0.0.1:12345/")));
        Assert.False(LeagueClientGateway.IsLeagueLoopbackRequest(new Uri("https://localhost:12345/")));
        Assert.False(LeagueClientGateway.IsLeagueLoopbackRequest(new Uri("http://127.0.0.1:12345/")));
        Assert.False(LeagueClientGateway.IsLeagueLoopbackRequest(new Uri("https://example.com/")));
    }

    [Theory]
    [InlineData("[]", false)]
    [InlineData("[{}]", true)]
    [InlineData("{}", false)]
    public void InventoryReadiness_RequiresAPopulatedCatalog(string json, bool expected)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(expected, LeagueClientGateway.IsInventoryPayloadReady(document.RootElement));
    }

    [Fact]
    public void LeagueSnapshot_RequiresBothInventoryCategoriesToBeComplete()
    {
        var snapshot = Snapshot(MatchSnapshotResult.Failed);
        Assert.False(snapshot.HasCompleteInventory);
        Assert.True(new LeagueSnapshot
        {
            Puuid = "puuid", RiotGameName = "Player", RiotTagLine = "EUW", Region = "EUW1",
            Champions = [], Skins = []
        }.HasCompleteInventory);
    }

    [Fact]
    public async Task LeagueLockfileReader_AllowsTheClientToKeepItsWriteHandleOpen()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "LeagueClient:1234:2999:secret:https");
            await using var clientHandle = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read);
            await Assert.ThrowsAsync<IOException>(() => File.ReadAllTextAsync(path));
            Assert.Equal("LeagueClient:1234:2999:secret:https", await LeagueClientGateway.ReadLockfileAsync(path));
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
        var result = LeagueMatchHistoryParser.Parse(json);
        Assert.Equal(hasMatch, result.HasMatch); Assert.Equal((long?)matchId, result.MatchId);
        if (json.Contains("[]", StringComparison.Ordinal)) Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task EncryptedRepository_HidesSecrets_AndPreservesMonotonicMatchHistory()
    {
        var root = Path.Combine(Path.GetTempPath(), "SummonersVaultTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new VaultPaths(root); var key = RandomNumberGenerator.GetBytes(32);
            await using var repository = new EncryptedSqliteVaultRepository(paths);
            await repository.OpenAsync(key, create: true);
            var account = new VaultAccount { LoginIdentifier = "plaintext-login-marker", PasswordUtf8 = Encoding.UTF8.GetBytes("plaintext-password-marker"), Region = "EUW1", Roles = AccountRole.Mid | AccountRole.Support };
            account.Champions.Add(new(1, "Annie")); account.Skins.Add(new(1000, 1, "Annie")); account.Skins.Add(new(1001, 1, "Goth Annie")); await repository.SaveAccountAsync(account);
            var newest = new DateTimeOffset(2026, 8, 5, 22, 30, 0, TimeSpan.Zero);
            await repository.ApplyLeagueSnapshotAsync(account.Id, Snapshot(MatchSnapshotResult.Known(newest, 42)));
            await repository.ApplyLeagueSnapshotAsync(account.Id, Snapshot(MatchSnapshotResult.Known(newest.AddDays(-1), 41)));
            var loaded = await repository.GetAccountAsync(account.Id);
            Assert.Equal(newest, loaded?.LastMatchPlayedAtUtc); Assert.Equal(42, loaded?.LastMatchId);
            await repository.ApplyLeagueSnapshotAsync(account.Id, Snapshot(MatchSnapshotResult.Failed));
            loaded = await repository.GetAccountAsync(account.Id);
            Assert.Equal(MatchHistoryState.Stale, loaded?.MatchHistoryState); Assert.Equal(newest, loaded?.LastMatchPlayedAtUtc);
            Assert.Equal("Goth Annie", Assert.Single(loaded!.Skins).Name);
            await repository.CloseAsync();
            var bytes = await File.ReadAllBytesAsync(paths.DatabasePath); var text = Encoding.UTF8.GetString(bytes);
            Assert.DoesNotContain("plaintext-login-marker", text, StringComparison.Ordinal); Assert.DoesNotContain("plaintext-password-marker", text, StringComparison.Ordinal);
            await Assert.ThrowsAnyAsync<Exception>(async () => { await using var wrong = new EncryptedSqliteVaultRepository(paths); await wrong.OpenAsync(RandomNumberGenerator.GetBytes(32), create: false); });
            CryptographicOperations.ZeroMemory(key);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Backup_ImportsAcrossDifferentMasterPasswords_AndPreservesMatchMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "SummonersVaultTests", Guid.NewGuid().ToString("N"));
        var sourceRoot = Path.Combine(root, "source"); var targetRoot = Path.Combine(root, "target"); var archive = Path.Combine(root, "portable.svault");
        Directory.CreateDirectory(root);
        try
        {
            var sourcePaths = new VaultPaths(sourceRoot); var sourceRepository = new EncryptedSqliteVaultRepository(sourcePaths); await using var sourceSession = new VaultSession(sourcePaths, sourceRepository);
            await sourceSession.CreateAsync("source password"u8.ToArray());
            var account = new VaultAccount { LoginIdentifier = "backup-user", PasswordUtf8 = "backup-secret"u8.ToArray(), Region = "NA1", LastMatchPlayedAtUtc = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero), LastMatchId = 999, MatchHistoryState = MatchHistoryState.Stale };
            await sourceRepository.SaveAccountAsync(account);
            await new VaultBackupService(sourcePaths, sourceSession).ExportAsync(archive);

            var targetPaths = new VaultPaths(targetRoot); var targetRepository = new EncryptedSqliteVaultRepository(targetPaths); await using var targetSession = new VaultSession(targetPaths, targetRepository);
            await targetSession.CreateAsync("target password"u8.ToArray());
            var targetBackup = new VaultBackupService(targetPaths, targetSession);
            await using (var preview = await targetBackup.PreviewImportAsync(archive, "source password"u8.ToArray())) await targetBackup.ImportAsync(preview, new Dictionary<Guid, BackupConflictChoice>());
            var imported = Assert.Single(await targetRepository.GetAccountsAsync());
            Assert.Equal(account.LastMatchPlayedAtUtc, imported.LastMatchPlayedAtUtc); Assert.Equal(999, imported.LastMatchId); Assert.Equal(MatchHistoryState.Stale, imported.MatchHistoryState);
            var withPassword = await targetRepository.GetAccountAsync(imported.Id, includePassword: true); Assert.Equal("backup-secret", Encoding.UTF8.GetString(withPassword!.PasswordUtf8));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static LeagueSnapshot Snapshot(MatchSnapshotResult match) => new() { Puuid = "puuid", RiotGameName = "Player", RiotTagLine = "EUW", Region = "EUW1", Match = match };
}
