# SummonersVault

[![Release downloads](https://img.shields.io/endpoint?url=https%3A%2F%2Fraw.githubusercontent.com%2FRVerheggen%2FSummonersVault%2Fdownload-statistics%2Fdownload-count.json&logo=github&cacheSeconds=3600)](https://github.com/RVerheggen/SummonersVault/releases)

SummonersVault is a free, open-source, local-only Windows password manager and read-only League Client companion. It keeps account credentials, notes, role tags, and offline League snapshots in an encrypted SQLite3MC database. Stored snapshots include ranks, owned champions and skins, wallet balances, crafting materials, and last-played match information.

[Website](https://rverheggen.github.io/SummonersVault) | [Releases](https://github.com/RVerheggen/SummonersVault/releases) | [Privacy](PRIVACY.md) | [Terms](TERMS.md) | [Source](https://github.com/RVerheggen/SummonersVault) | [Security](SECURITY.md) | [Third-party notices](THIRD_PARTY_NOTICES.md)

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

The release-download badge counts installer, portable ZIP, and Velopack full or delta package downloads across stable GitHub Releases. It excludes update-feed metadata, checksums, and release notes. This is a download count, not a count of unique users or installations. Re-downloads, update fallbacks, and automated release preparation can increase it. SummonersVault does not collect installation or usage telemetry.

SummonersVault can check the public GitHub Releases feed shortly after startup, at most once every 24 hours. It does not run an update service or background process while closed. Update downloads and installation always require user approval, and automatic checks can be disabled in Settings. The installed version and a manual `Check for updates` action are always available in Settings.

Releases are currently unsigned, so Windows may display a SmartScreen warning. Review the public source and release workflow before running the application. Each release includes SHA-256 checksums and GitHub build provenance. With the GitHub CLI installed, a downloaded artifact can be checked using:

```powershell
gh attestation verify .\SummonersVault.Desktop-win-Setup.exe -R RVerheggen/SummonersVault
```

See [the release guide](docs/releasing.md) for local packaging, checksum verification, and the public-release security checklist.

## Architecture and contributing

The solution is divided into Core, Application, Infrastructure, and WPF presentation projects. Local encrypted persistence uses EF Core over the SQLite3MC connection, with migrations checked into source control. See [the architecture and database guide](docs/architecture.md) for dependency boundaries, public schema v4 adoption, backup compatibility, and required contributor checks.

## License, forks, and branding

SummonersVault source code is licensed under the [GNU General Public License version 3 only](LICENSE), except where another license or ownership notice is stated. Distributed modifications must remain available under GPLv3 and include the corresponding source code.

The SummonersVault name and application icon identify the official project and are not licensed as branding for modified products. Forks and independently published builds must use a distinct user-facing name and icon, may accurately state that they are based on SummonersVault, and must not imply endorsement by the original project. See [the trademark and branding policy](TRADEMARKS.md), [project notice](NOTICE), and [third-party notices](THIRD_PARTY_NOTICES.md).

## Security model

The master password must contain at least 8 Unicode characters; a 12+ character passphrase is recommended. There is no recovery key. The complete database is encrypted with a random key protected by an Argon2id-derived key and an AES-256-GCM envelope. See [docs/security.md](docs/security.md) for limitations.

SummonersVault never logs in for you, sends credentials to Riot, or modifies the client or game. It uses only read-only local League Client API requests to `127.0.0.1` and does not use or embed a Riot Web API key. League Client API endpoints are unsupported interfaces and can change without notice.

Champion, skin, rank, wallet, and crafting snapshots are read from the signed-in local League Client. Public champion, skin, and loot artwork can be downloaded from CommunityDragon and is cached locally up to 256 MB. These requests contain only public asset paths - no account identifiers, credentials, PUUIDs, or League Client tokens are sent. CommunityDragon downloads can be disabled and the cache can be cleared in Settings.

### Why login is not automated

SummonersVault deliberately does not automatically enter credentials or submit the Riot Client login form. Riot's [Terms of Service](https://www.riotgames.com/en/terms-of-service) broadly prohibit unauthorized automation programs that interact with Riot Services. Even though password autofill would provide no gameplay advantage, Riot does not publicly identify this use as authorized. Avoiding login automation reduces the risk of users receiving account penalties, including a potential suspension or ban.

## Riot registration status

SummonersVault is registered as a Production application in the Riot Developer Portal. Product URL verification succeeded, and the application is currently **Pending review** by Riot.

Registration and URL verification do not mean that Riot has approved, endorsed, certified, or audited SummonersVault. The current integration uses only read-only local League Client API requests and no Riot Web API key. See the [registration record](docs/riot-registration.md) for the public status details.

## Fan-project notice

SummonersVault isn't endorsed by Riot Games and doesn't reflect the views or opinions of Riot Games or anyone officially involved in producing or managing Riot Games properties. Riot Games and all associated properties are trademarks or registered trademarks of Riot Games, Inc.
