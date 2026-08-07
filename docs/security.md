# Security model

SummonersVault is a local, single-user vault. A random database key encrypts the complete SQLite file. The master password derives a wrapping key with Argon2id and decrypts that database key through an authenticated AES-256-GCM envelope. Changing the master password replaces only the envelope.

This protects closed vault files and portable backups against casual disclosure and offline inspection. It cannot protect secrets from malware, debuggers, administrators, screenshots, clipboard monitors, or memory inspection while the vault is unlocked. There is no recovery key: losing the master password permanently loses access to the vault and its backups.

League Client credentials from its lockfile are ephemeral, remain in memory, and are never persisted. The integration accepts only loopback HTTPS and performs GET requests only.

