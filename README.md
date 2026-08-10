# SummonersVault

SummonersVault is a free, local-only Windows password manager and read-only League Client companion. It keeps account credentials, notes, role tags, offline League snapshots, owned champions and skins, ranks, and last-played match information in an encrypted SQLite3MC database.

## Build and run

Requires the .NET 10 SDK and Windows 11 x64.

```powershell
dotnet restore
dotnet build SummonersVault.slnx
dotnet run --project src/SummonersVault.App/SummonersVault.App.csproj
```

Create a self-contained folder release with:

```powershell
dotnet publish src/SummonersVault.App/SummonersVault.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false
```

## Install and update

Public releases provide two Windows 11 x64 downloads:

- `SummonersVault.Desktop-win-Setup.exe` is the recommended per-user installer. It requires no administrator access and installs application files beneath `%LOCALAPPDATA%\SummonersVault.Desktop`.
- `SummonersVault.Desktop-win-Portable.zip` is intended for advanced users and testers.

Both packages are self-contained and do not require a separate .NET installation. Vault data remains beneath `%LOCALAPPDATA%\SummonersVault` and is not replaced by application updates.

SummonersVault can check the public GitHub Releases feed shortly after startup, at most once every 24 hours. It does not run an update service or background process while closed. Update downloads and installation always require user approval, and automatic checks can be disabled in Settings. The installed version and a manual `Check for updates` action are always available in Settings.

Releases are currently unsigned, so Windows may display a SmartScreen warning. Review the public source and release workflow before running the application. Each release includes SHA-256 checksums and GitHub build provenance. With the GitHub CLI installed, a downloaded artifact can be checked using:

```powershell
gh attestation verify .\SummonersVault.Desktop-win-Setup.exe -R RVerheggen/SummonersVault
```

See [the release guide](docs/releasing.md) for local packaging, checksum verification, and the public-release security checklist.

## Security model

The master password must contain at least 8 Unicode characters; a 12+ character passphrase is recommended. There is no recovery key. The complete database is encrypted with a random key protected by an Argon2id-derived key and an AES-256-GCM envelope. See [docs/security.md](docs/security.md) for limitations.

SummonersVault never logs in for you, sends credentials to Riot, modifies the client or game, or requires a Riot API key. Its LCU connection is loopback-only and read-only. LCU endpoints are unsupported community interfaces and can change without notice.

Champion, skin, rank, wallet, and crafting snapshots are read from the signed-in local League Client. Public champion, skin, and loot artwork can be downloaded from CommunityDragon and is cached locally up to 256 MB. These requests contain only public asset paths - no account identifiers, credentials, PUUIDs, or League Client tokens are sent. CommunityDragon downloads can be disabled and the cache can be cleared in Settings.

### Why login is not automated

SummonersVault deliberately does not automatically enter credentials or submit the Riot Client login form. Riot's [Terms of Service](https://www.riotgames.com/en/terms-of-service) broadly prohibit unauthorized automation programs that interact with Riot Services. Even though password autofill would provide no gameplay advantage, Riot does not publicly identify this use as authorized. Avoiding login automation reduces the risk of users receiving account penalties, including a potential suspension or ban.

## Fan-project notice

SummonersVault isn't endorsed by Riot Games and doesn't reflect the views or opinions of Riot Games or anyone officially involved in producing or managing Riot Games properties. Riot Games and all associated properties are trademarks or registered trademarks of Riot Games, Inc.

LCU use must be registered with Riot before a public release.
