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

## Security model

The master password must contain at least 8 Unicode characters; a 12+ character passphrase is recommended. There is no recovery key. The complete database is encrypted with a random key protected by an Argon2id-derived key and an AES-256-GCM envelope. See [docs/security.md](docs/security.md) for limitations.

SummonersVault never logs in for you, sends credentials to Riot, modifies the client or game, or requires a Riot API key. Its LCU connection is loopback-only and read-only. LCU endpoints are unsupported community interfaces and can change without notice.

## Fan-project notice

SummonersVault isn't endorsed by Riot Games and doesn't reflect the views or opinions of Riot Games or anyone officially involved in producing or managing Riot Games properties. Riot Games and all associated properties are trademarks or registered trademarks of Riot Games, Inc.

LCU use must be registered with Riot before a public release.
