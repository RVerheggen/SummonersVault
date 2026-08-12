---
layout: default
title: SummonersVault
description: A local encrypted password manager and read-only League Client companion for Windows.
---

<p align="center">
  <img src="src/SummonersVault.App/Assets/Branding/AppIcon-1254.png" alt="SummonersVault application icon" width="112">
</p>

# SummonersVault

SummonersVault is a free, open-source Windows application for managing encrypted local account credentials and offline League snapshots. It does not require a SummonersVault account, cloud service, or telemetry connection.

[Download releases](https://github.com/RVerheggen/SummonersVault/releases) | [Inspect the source](https://github.com/RVerheggen/SummonersVault) | [Privacy](PRIVACY.md) | [Terms](TERMS.md)

## What it provides

- An encrypted local vault for usernames, passwords, notes, regions, and role tags.
- Read-only snapshots of ranks, owned champions, owned skins, RP, Blue Essence, crafting materials, and last-played information.
- Search and filtering across stored account and collection information.
- Offline review of previously synchronized snapshots without signing into every account again.
- Local backup and restore support for the encrypted vault.

Snapshots reflect the last successful synchronization. Users always sign in manually through the official Riot Client. SummonersVault does not automate login, submit credentials to Riot, modify the client, or automate gameplay.

## Local and read-only by design

League information is requested only from read-only League Client API endpoints on `127.0.0.1` while the official client is running. SummonersVault does not use or distribute a Riot Web API key. Vault data remains on the user's computer, and the project operates no analytics, advertising, telemetry, cloud account, or synchronization service.

Public artwork can optionally be downloaded from CommunityDragon and cached locally. Artwork downloads can be disabled and the cache can be cleared in Settings. Update checks use the public GitHub Releases feed and can also be disabled.

## Riot registration status

SummonersVault is registered as a Production application in the Riot Developer Portal. The registered Product URL is this website, URL verification succeeded, and the application is currently **Pending review**.

Registration and URL verification are not Riot approval, endorsement, certification, or a security audit. See the [public registration record](docs/riot-registration.md).

## Fan-project notice

SummonersVault isn't endorsed by Riot Games and doesn't reflect the views or opinions of Riot Games or anyone officially involved in producing or managing Riot Games properties. Riot Games and all associated properties are trademarks or registered trademarks of Riot Games, Inc.

## Project documents

- [Privacy policy](PRIVACY.md)
- [Usage terms](TERMS.md)
- [Security policy](https://github.com/RVerheggen/SummonersVault/blob/main/SECURITY.md)
- [Security model](https://github.com/RVerheggen/SummonersVault/blob/main/docs/security.md)
- [GNU GPLv3 license](https://github.com/RVerheggen/SummonersVault/blob/main/LICENSE)
- [Trademark and branding policy](https://github.com/RVerheggen/SummonersVault/blob/main/TRADEMARKS.md)
- [Third-party notices](https://github.com/RVerheggen/SummonersVault/blob/main/THIRD_PARTY_NOTICES.md)
