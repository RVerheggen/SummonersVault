# SummonersVault 1.0.0

First stable release of SummonersVault.

- Refactor the application into focused Core, Application, Infrastructure, and WPF presentation projects.
- Replace hand-written persistence with EF Core while retaining SQLite3MC full-database encryption and compatibility with public schema version 4 vaults and backups.
- Keep passwords outside normal account queries and retrieve them only through disposable sensitive buffers.
- Preserve read-only League Client synchronization, encrypted backup import and export, artwork caching, and Velopack updates.
- Protect account loading from EF Core cartesian query explosions with explicit and global split-query behavior plus regression tests.
- Prevent repeated unlock attempts from displaying an incorrect password error after a successful unlock.
- Restore keyboard focus to the master-password field after an unsuccessful unlock attempt.
- Add repository-wide code-style enforcement, architecture documentation, migration validation, and expanded persistence and security tests.
