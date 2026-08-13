# Architecture

SummonersVault uses four projects with dependencies pointing inward:

- `SummonersVault.Core` contains account and League snapshot models plus pure matching and normalization rules.
- `SummonersVault.Application` defines persistence, vault, backup, artwork, settings, and League boundaries. It also contains account, vault, and synchronization workflows.
- `SummonersVault.Infrastructure` implements encrypted EF Core persistence, the local League Client integration, backup archives, artwork caching, settings, and key management.
- `SummonersVault.App` contains WPF presentation, clipboard handling, Velopack integration, and the dependency-injection composition root.

The WPF layer does not access EF Core or SQLite directly. Ordinary account queries never return passwords. A password is retrieved only through `SensitiveBuffer`, which clears its owned byte array when disposed. Account saves carry an optional password separately from the normal `VaultAccount` model.

## Encrypted persistence

The vault uses EF Core 10 with `Microsoft.EntityFrameworkCore.Sqlite.Core`, `Microsoft.Data.Sqlite.Core`, and the SQLite3MC native bundle. The standard unencrypted SQLite bundle is intentionally not referenced.

One non-pooled encrypted `SqliteConnection` remains open only while the vault is unlocked. Repository operations create short-lived `VaultDbContext` instances over that connection. Read queries are no-tracking by default, while transactional writes use a dedicated tracking context when needed.

Fresh databases are created only through checked-in EF migrations. `EnsureCreated` is not used. CI verifies that the EF model has no pending changes.

## Public schema v4 adoption

Public releases `v0.1.0`, `v0.1.1`, and `v0.1.2` used schema version 4 before EF Core migration history was introduced. On first successful unlock, SummonersVault:

1. Authenticates and opens the encrypted database.
2. Requires `schema_info.version` to equal 4.
3. Validates the required v4 tables and columns.
4. Atomically creates EF migration history, records `InitialCurrentSchema`, and removes `schema_info`.
5. Applies any newer EF migrations.

Pre-release schemas v1 through v3 and malformed v4 databases are rejected without modification. Migration failures close the connection and leave the vault locked. The EF migration lock is never deleted automatically.

## Backups

The `.svault` archive remains format version 1. It contains the encrypted database, its encrypted-key metadata, and a non-sensitive checksum manifest. Existing public schema v4 backups are adopted through the same validated path after the backup master password opens them. New EF-managed backups open normally.

Import conflict precedence remains linked PUUID first, then normalized username and region. Merge writes are transactional, and credentials travel through disposable sensitive buffers.

## Contributor checks

Run these checks before submitting changes:

```powershell
dotnet tool restore
dotnet restore SummonersVault.slnx
dotnet format SummonersVault.slnx --no-restore --verify-no-changes
dotnet build SummonersVault.slnx -c Release --no-restore
dotnet ef migrations has-pending-model-changes --project src/SummonersVault.Infrastructure/SummonersVault.Infrastructure.csproj --context VaultDbContext --configuration Release --no-build
dotnet test SummonersVault.slnx -c Release --no-build
```

Use `dotnet ef migrations add <Name>` against the Infrastructure project for schema changes. Never use `EnsureCreated`, hand-edit an existing released migration, or add the standard SQLite native bundle.
