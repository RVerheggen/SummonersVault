---
layout: default
title: SummonersVault privacy policy
---

# SummonersVault privacy policy

Last updated: 12 August 2026

This policy describes the network activity and local data handling of the official SummonersVault desktop application. SummonersVault is a local-only application and the project does not operate user accounts, analytics, advertising, telemetry, cloud storage, or a synchronization service.

## Data stored on your computer

Vault credentials, notes, settings, and synchronized League snapshots are stored on the user's computer. Credentials and vault data are kept in the encrypted local database beneath `%LOCALAPPDATA%\SummonersVault\Data`. Public artwork is cached separately beneath `%LOCALAPPDATA%\SummonersVault\Cache\Artwork`.

The master password, stored account passwords, database keys, and League Client lockfile tokens are not intentionally transmitted by SummonersVault. The League Client token is used in memory to authenticate local requests while the client is running.

No security design can protect data from every threat. In particular, malware, an administrator, screenshots, or memory inspection may access information while the vault is unlocked.

## Local League Client requests

SummonersVault connects only to read-only League Client API endpoints on `127.0.0.1`. These requests retrieve information from the official League Client running on the same computer. SummonersVault does not submit credentials to Riot and does not use or embed a Riot Web API key.

## GitHub update requests

Packaged versions can check the public [GitHub Releases](https://github.com/RVerheggen/SummonersVault/releases) feed for updates. Automatic checks can be disabled in Settings, and downloads require user approval.

GitHub receives the information normally associated with an HTTPS request, which may include an IP address, request time, request headers, and the requested release file. GitHub handles that information under the [GitHub General Privacy Statement](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement). SummonersVault does not add vault contents or account credentials to update requests.

## CommunityDragon artwork requests

When CommunityDragon artwork downloads are enabled, SummonersVault requests public artwork paths from `raw.communitydragon.org`. The requested URL contains a public League asset path. SummonersVault does not intentionally include account identifiers, PUUIDs, credentials, database keys, or League Client tokens in these URLs.

Like any network service, CommunityDragon can receive information normally associated with a request, such as an IP address, request time, and request headers. CommunityDragon is an independent community service. See its [asset documentation](https://communitydragon.org/documentation/assets) and [public asset service](https://raw.communitydragon.org/latest/).

CommunityDragon downloads can be disabled in Settings. Existing cached artwork can be removed with the `Clear artwork cache` action.

## Removing local data

Close SummonersVault, then delete `%LOCALAPPDATA%\SummonersVault` to remove the vault database, settings, metadata, and artwork cache maintained in the default data location. Exported `.svault` backups and other files manually saved elsewhere must be deleted separately.

Uninstalling the application removes installed program files but may intentionally leave the local data directory in place so an update or reinstall does not erase the vault.

## Contact

For privacy questions, open an issue in the [SummonersVault issue tracker](https://github.com/RVerheggen/SummonersVault/issues). Do not include passwords, vault files, League lockfiles, tokens, or other sensitive data in a public issue. Security vulnerabilities should be reported using the process in the [security policy](SECURITY.md).
