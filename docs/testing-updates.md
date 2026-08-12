# Testing SummonersVault updates

SummonersVault checks the stable releases in `https://github.com/RVerheggen/SummonersVault`. An application started from Visual Studio or `dotnet run` is not a Velopack package and cannot update itself.

There are two different tests:

1. Local package verification confirms that the application publishes and Velopack produces valid files.
2. The GitHub end-to-end test confirms the real check, prompt, download, install, restart, and data-preservation flow.

## Important limitations

- The current application uses the production GitHub Releases feed. It does not have a local-folder or staging-feed switch.
- The release workflow refuses to publish while the repository is private.
- The application ignores draft and prerelease releases because it uses the stable channel.
- Every tag used for the end-to-end test creates a real public version. Do not use throwaway version numbers if immutable releases are enabled.
- Complete Riot registration and the public-release checks in [Releasing SummonersVault](releasing.md) before publicly distributing a test build.

## Part 1: Verify packaging locally

Run these commands from the repository root:

```powershell
dotnet restore
dotnet tool restore
dotnet test SummonersVault.slnx -c Release --no-restore
dotnet publish src/SummonersVault.App/SummonersVault.App.csproj -c Release -r win-x64 --self-contained true --no-restore -o artifacts/publish/win-x64
dotnet tool install --tool-path artifacts/tools vpk --version 1.2.0
.\artifacts\tools\vpk.exe pack --packId SummonersVault.Desktop --packTitle SummonersVault --packVersion 0.1.0 --packDir artifacts\publish\win-x64 --mainExe SummonersVault.App.exe --runtime win-x64 --icon src\SummonersVault.App\Assets\AppIcon.ico --releaseNotes RELEASE_NOTES.md --outputDir artifacts\releases
```

Confirm that `artifacts/releases` contains an installer, portable ZIP, full package, and Velopack release index. Run the installer and confirm:

- Installation does not request administrator access.
- The application is installed beneath `%LOCALAPPDATA%\SummonersVault.Desktop`.
- Settings displays `Version 0.1.0`.
- Settings does not display `Updates unavailable in development builds`.
- Launching the executable directly from `artifacts/publish/win-x64` does display the development-build message. This is expected because that folder is published but not installed by Velopack.

This verifies packaging, but the local installation will still check GitHub for updates.

## Part 2: Publish and install the baseline version

Use `0.1.0` as the example baseline. Replace it with the actual version if necessary.

1. Confirm the repository is public and the release gate in [Releasing SummonersVault](releasing.md) is complete.
2. Confirm the application project contains `<Version>0.1.0</Version>`.
3. Confirm `RELEASE_NOTES.md` describes version `0.1.0`.
4. Confirm the release commit is on `main` and CI is passing.
5. Create and push the matching tag:

   ```powershell
   git switch main
   git pull --ff-only origin main
   git tag -a v0.1.0 -m "SummonersVault 0.1.0"
   git push origin v0.1.0
   ```

6. Wait for the GitHub `Release` workflow to finish successfully.
7. Open the `v0.1.0` GitHub Release and confirm it contains the setup executable, portable ZIP, full package, release index, checksums, and provenance attestations.
8. Download the setup executable from the release. Do not use a locally built installer for this end-to-end test.
9. Install and launch the application.
10. Create a disposable test vault, add a test account containing synthetic credentials, and optionally export a backup.
11. Close and reopen the application. Confirm the vault starts locked and the test data remains available after unlocking.

The first packaged version should report that it is up to date because no newer stable release exists yet.

## Part 3: Publish the update version

Use `0.1.1` as the example update.

1. Change the application project to `<Version>0.1.1</Version>`.
2. Update `RELEASE_NOTES.md` to `SummonersVault 0.1.1` and describe a small, verifiable change.
3. Commit the version and release-note changes, merge them into `main`, and wait for CI to pass.
4. Create and push the matching tag:

   ```powershell
   git switch main
   git pull --ff-only origin main
   git tag -a v0.1.1 -m "SummonersVault 0.1.1"
   git push origin v0.1.1
   ```

5. Wait for the GitHub `Release` workflow to complete.
6. Confirm the `v0.1.1` release is public, stable, and contains the expected Velopack assets.

## Part 4: Exercise the update flow

On the computer with the installed `0.1.0` version:

1. Launch SummonersVault and unlock the disposable test vault.
2. Open Settings and confirm it displays `Version 0.1.0`.
3. Select `Check for updates`. Manual checks bypass the 24-hour automatic-check interval.
4. Confirm the update dialog displays version `0.1.1` and the expected release notes.
5. Select `Later` first. Confirm no download starts and the application remains on `0.1.0`.
6. Select `Check for updates` again, then select `Download and install`.
7. Confirm progress is displayed. Cancellation may be tested before installation begins.
8. Allow installation to start. The application should lock the vault, close its database session, clear app-owned clipboard content, exit, apply the update, and restart.
9. Confirm the restarted application is locked.
10. Open Settings and confirm it displays `Version 0.1.1`.
11. Unlock the vault and confirm the synthetic account, settings, backup, and cached artwork remain available.
12. Confirm the small visible change described in the `0.1.1` release notes.
13. Select `Check for updates` again and confirm version `0.1.1` is reported as current.

Persistent data should remain beneath `%LOCALAPPDATA%\SummonersVault`. Application binaries should remain beneath `%LOCALAPPDATA%\SummonersVault.Desktop`.

## Part 5: Test the portable package

1. Download and extract the `0.1.0` portable ZIP into its own test folder.
2. Launch it and confirm Settings displays version `0.1.0`.
3. After `0.1.1` is published, select `Check for updates`.
4. Accept the update and confirm it restarts as version `0.1.1`.
5. Confirm the portable test does not interfere with the installed copy.

Use synthetic data for the portable test as well.

## Failure checks

These checks do not require another release:

- Disconnect networking and run a manual check. Settings should show a clean connection error without blocking startup.
- Cancel an update download before installation. The installed version should remain unchanged.
- Start the application from Visual Studio and run a manual check. Settings should show `Updates unavailable in development builds`.
- Disable `Automatically check for updates`, restart the application, and confirm no automatic check occurs. Manual checks must still work.
- Re-enable automatic checks and confirm a successful check records its time. Another automatic check should not run for 24 hours.

## Pass criteria

The update test passes when:

- The baseline package detects exactly the newer stable version.
- Release notes, progress, cancellation, and `Later` behave correctly.
- Applying the update restarts into a locked state.
- The displayed version changes to the target version.
- Vault data, settings, backups, and artwork cache survive the update.
- No administrator access, Windows service, scheduled task, tray agent, or process left running after exit is introduced.
- A manual check reports the updated version as current.

Do not delete a published release that active installations may still need. Record the tested source version, target version, Windows version, installer or portable mode, and result in the release checklist.
