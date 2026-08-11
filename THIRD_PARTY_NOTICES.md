# Third-party notices

This file identifies major third-party components and assets used by
SummonersVault. It is informational and does not replace the license terms or
policies supplied by their owners. If this summary conflicts with an upstream
license, the upstream license controls.

## Riot Games and League of Legends

League of Legends, Riot Games, and their associated names, game data, artwork,
and trademarks are owned by Riot Games, Inc. They are not licensed under the
SummonersVault GPLv3 license.

League-related images stored under these paths are used as game-specific static
assets and remain subject to Riot's terms and policies:

- `src/SummonersVault.App/Assets/Currencies/`
- `src/SummonersVault.App/Assets/RankIcons/`

Champion, skin, profile, and crafting artwork retrieved at runtime from the
local League Client or CommunityDragon is not part of the SummonersVault source
code license. Cached artwork remains subject to its original ownership terms.

SummonersVault isn't endorsed by Riot Games and doesn't reflect the views or
opinions of Riot Games or anyone officially involved in producing or managing
Riot Games properties. Riot Games and all associated properties are trademarks
or registered trademarks of Riot Games, Inc.

Distributors of modified versions are independently responsible for complying
with Riot's policies and registering their own League Client API usage where
required.

## SummonersVault branding

The SummonersVault name, application icon, and branding assets under
`src/SummonersVault.App/Assets/Branding/` and
`src/SummonersVault.App/Assets/AppIcon.ico` are not included in the GPLv3
license grant for modified product branding. See `TRADEMARKS.md` for permitted
referential use and fork requirements.

## Runtime dependencies

| Component | Version | License | Project |
| --- | --- | --- | --- |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | https://github.com/CommunityToolkit/dotnet |
| Microsoft.Data.Sqlite.Core | 10.0.10 | MIT | https://github.com/dotnet/efcore |
| Microsoft.Extensions.DependencyInjection | 10.0.0 | MIT | https://github.com/dotnet/runtime |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.0 | MIT | https://github.com/dotnet/runtime |
| NSec.Cryptography | 26.4.0 | MIT | https://nsec.rocks/ |
| libsodium | 1.0.22 | ISC | https://libsodium.org/ |
| SQLite3MC.PCLRaw.bundle | 2.4.0 | MIT | https://utelle.github.io/SQLite3MultipleCiphers/ |
| SQLite3MC.PCLRaw.lib | 2.4.0 | MIT | https://utelle.github.io/SQLite3MultipleCiphers/ |
| SQLite3MC.PCLRaw.provider | 2.4.0 | MIT | https://utelle.github.io/SQLite3MultipleCiphers/ |
| SQLitePCLRaw.core | 3.0.2 | Apache-2.0 | https://github.com/ericsink/SQLitePCL.raw |
| Velopack | 1.2.0 | MIT | https://github.com/velopack/velopack |

The self-contained Windows distribution also contains .NET and WPF runtime
components under their applicable Microsoft and third-party licenses.

## Development and test dependencies

| Component | Version | License | Project |
| --- | --- | --- | --- |
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT | https://github.com/microsoft/vstest |
| xunit | 2.9.3 | Apache-2.0 | https://github.com/xunit/xunit |
| xunit.runner.visualstudio | 3.1.5 | Apache-2.0 | https://github.com/xunit/visualstudio.xunit |

Complete license texts and notices for packaged dependencies can also be found
in their respective NuGet packages and upstream repositories.
