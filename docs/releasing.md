# Releasing SummonersVault

SummonersVault uses Velopack 1.2.0 and GitHub Releases. The package ID is permanently `SummonersVault.Desktop`. Do not reuse `%LOCALAPPDATA%\SummonersVault` as the application installation directory because it contains persistent vault data.

## Public-release gate

Complete these checks before making the repository public or pushing the first version tag:

1. Scan the complete Git history for credentials, API tokens, private keys, environment files, databases, `.svault` files, League lockfiles, and signing files.
2. Confirm values containing `password`, `secret`, or `token` in tests are synthetic fixtures.
3. Rotate any real secret before removing it from Git history.
4. Review all outbound HTTP destinations and the loopback-only League certificate exception.
5. Review native interop. `AllowUnsafeBlocks` is scoped to the WPF project because the source-generated `LibraryImport` in `DarkTitleBar.cs` requires it for the DWM title-bar call.
6. Run the test suite, Release build, and self-contained publish.
7. Enable GitHub secret scanning, push protection, private vulnerability reporting, Dependabot, immutable releases, branch protection, required CI, and maintainer two-factor authentication.
8. Register LCU usage with Riot before public distribution.

## Create a release

1. Update the application `<Version>` and `RELEASE_NOTES.md` to the same SemVer value.
2. Merge the release changes into protected `main` after CI and security checks pass.
3. Create and push a matching tag, for example `v0.1.0`.
4. The release workflow validates the version, runs tests, publishes a self-contained `win-x64` build, creates Velopack installer and portable assets, generates SHA-256 checksums and provenance attestations, and publishes the GitHub Release.
5. Download the published installer and portable ZIP, verify their checksums and attestations, and perform a clean Windows 11 installation and update smoke test.

Do not push a version tag merely to validate compilation or packaging. Use the local package verification commands below for those checks. The installed application currently reads updates only from the public GitHub Releases feed, so a complete check, download, install, and restart test requires two real releases. Follow [Testing updates](testing-updates.md) before creating either release.

## Local package verification

```powershell
dotnet restore
dotnet tool restore
dotnet test SummonersVault.slnx -c Release
dotnet publish src/SummonersVault.App/SummonersVault.App.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64
dotnet tool install --tool-path artifacts/tools vpk --version 1.2.0
.\artifacts\tools\vpk.exe pack --packId SummonersVault.Desktop --packTitle SummonersVault --packVersion 0.1.0 --packDir artifacts\publish\win-x64 --mainExe SummonersVault.App.exe --runtime win-x64 --icon src\SummonersVault.App\Assets\AppIcon.ico --releaseNotes RELEASE_NOTES.md --outputDir artifacts\releases
```

The installer writes application files beneath `%LOCALAPPDATA%\SummonersVault.Desktop`. The encrypted vault, settings, backups, and artwork cache remain beneath `%LOCALAPPDATA%\SummonersVault` and are not part of the application package.

For the full update test, including the user prompt, download progress, vault locking, restart, and data preservation, see [Testing updates](testing-updates.md).

## Verify downloaded assets

Compare an asset with `SHA256SUMS.txt`, then verify its GitHub build provenance:

```powershell
Get-FileHash .\SummonersVault.Desktop-win-Setup.exe -Algorithm SHA256
gh attestation verify .\SummonersVault.Desktop-win-Setup.exe -R RVerheggen/SummonersVault
```

GitHub provenance links an artifact to a workflow, repository, commit, and triggering event. It is not Windows Authenticode signing and does not suppress SmartScreen.
