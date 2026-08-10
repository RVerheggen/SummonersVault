using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using Velopack;
using Velopack.Exceptions;
using Velopack.Sources;
using SummonersVault.Infrastructure.Settings;

namespace SummonersVault.App.Services;

internal enum UpdateCheckState
{
    Unavailable,
    UpToDate,
    Available,
    Failed
}

internal sealed record AvailableUpdate(
    string Version,
    string ReleaseNotes,
    object NativeUpdate);

internal sealed record UpdateCheckResult(
    UpdateCheckState State,
    string Message,
    AvailableUpdate? Update = null)
{
    public bool Succeeded => State is UpdateCheckState.UpToDate or UpdateCheckState.Available;
}

internal sealed record UpdateDownloadResult(bool Succeeded, bool Cancelled, string Message);

internal interface IUpdateService
{
    string CurrentVersion { get; }
    bool IsPackaged { get; }
    bool IsPortable { get; }
    Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default);
    Task<UpdateDownloadResult> DownloadAsync(AvailableUpdate update, IProgress<int> progress, CancellationToken cancellationToken = default);
    void ApplyAndRestart(AvailableUpdate update);
}

internal sealed class VelopackUpdateService : IUpdateService
{
    internal const string RepositoryUrl = "https://github.com/RVerheggen/SummonersVault";
    private readonly UpdateManager _manager;

    public VelopackUpdateService()
        : this(new UpdateManager(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false)))
    {
    }

    internal VelopackUpdateService(UpdateManager manager)
    {
        _manager = manager;
        var entryAssembly = Assembly.GetEntryAssembly();
        CurrentVersion = ResolveCurrentVersion(
            manager.CurrentVersion?.ToString(),
            entryAssembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            entryAssembly?.GetName().Version);
    }

    public string CurrentVersion { get; }
    public bool IsPackaged => _manager.IsInstalled;
    public bool IsPortable => _manager.IsPortable;

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        if (!IsPackaged)
            return new(UpdateCheckState.Unavailable, "Updates unavailable in development builds");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var update = await _manager.CheckForUpdatesAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            if (update is null)
                return new(UpdateCheckState.UpToDate, $"Version {CurrentVersion} is up to date");

            var asset = update.TargetFullRelease;
            var available = new AvailableUpdate(
                asset.Version.ToString(),
                string.IsNullOrWhiteSpace(asset.NotesMarkdown) ? "No release notes were provided for this version." : asset.NotesMarkdown,
                update);
            return new(UpdateCheckState.Available, $"Version {available.Version} is available", available);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (NotInstalledException)
        {
            return new(UpdateCheckState.Unavailable, "Updates unavailable in development builds");
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            return new(UpdateCheckState.Failed, "GitHub's update request limit was reached. Try again later.");
        }
        catch (HttpRequestException)
        {
            return new(UpdateCheckState.Failed, "Could not reach GitHub. Check your connection and try again.");
        }
        catch (TaskCanceledException)
        {
            return new(UpdateCheckState.Failed, "The update check timed out. Try again shortly.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            return new(UpdateCheckState.Failed, "The update information could not be read. Try again later.");
        }
        catch (Exception)
        {
            return new(UpdateCheckState.Failed, "The update check could not be completed. Try again later.");
        }
    }

    public async Task<UpdateDownloadResult> DownloadAsync(AvailableUpdate update, IProgress<int> progress, CancellationToken cancellationToken = default)
    {
        if (update.NativeUpdate is not UpdateInfo nativeUpdate)
            return new(false, false, "The selected update is no longer valid. Check for updates again.");

        try
        {
            await _manager.DownloadUpdatesAsync(nativeUpdate, value => progress.Report(value), cancellationToken).ConfigureAwait(false);
            return new(true, false, "Update ready to install");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(false, true, "Update download cancelled");
        }
        catch (ChecksumFailedException)
        {
            return new(false, false, "The downloaded update failed its integrity check. Nothing was installed.");
        }
        catch (AcquireLockFailedException)
        {
            return new(false, false, "Another update operation is already running.");
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            return new(false, false, "GitHub's download request limit was reached. Try again later.");
        }
        catch (HttpRequestException)
        {
            return new(false, false, "The update could not be downloaded. Check your connection and try again.");
        }
        catch (TaskCanceledException)
        {
            return new(false, false, "The update download timed out. Try again shortly.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotInstalledException)
        {
            return new(false, false, "The update could not be prepared. Nothing was installed.");
        }
        catch (Exception)
        {
            return new(false, false, "The update could not be downloaded. Nothing was installed.");
        }
    }

    public void ApplyAndRestart(AvailableUpdate update)
    {
        if (update.NativeUpdate is not UpdateInfo nativeUpdate)
            throw new InvalidOperationException("The selected update is no longer valid. Check for updates again.");

        _manager.ApplyUpdatesAndRestart(nativeUpdate.TargetFullRelease);
    }

    internal static string ResolveCurrentVersion(string? packagedVersion, string? informationalVersion, Version? assemblyVersion)
    {
        if (!string.IsNullOrWhiteSpace(packagedVersion)) return packagedVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion)) return informationalVersion.Split('+', 2)[0];
        return assemblyVersion?.ToString(3) ?? "0.1.0";
    }
}

internal sealed class UpdateWorkflow(IUpdateService updateService)
{
    internal bool ShouldRunAutomaticCheck(AppSettings settings, DateTimeOffset nowUtc) =>
        settings.AutomaticallyCheckForUpdates
        && updateService.IsPackaged
        && (settings.LastUpdateCheckAtUtc is null || nowUtc - settings.LastUpdateCheckAtUtc >= TimeSpan.FromHours(24));

    internal async Task<UpdateCheckResult> CheckAsync(
        AppSettings settings,
        bool manual,
        DateTimeOffset nowUtc,
        Func<CancellationToken, Task> persistSettings,
        CancellationToken cancellationToken = default)
    {
        if (!manual && !ShouldRunAutomaticCheck(settings, nowUtc))
            return new(UpdateCheckState.Unavailable, "An automatic update check is not due yet");

        var result = await updateService.CheckForUpdatesAsync(cancellationToken);
        if (result.Succeeded)
        {
            settings.LastUpdateCheckAtUtc = nowUtc;
            await persistSettings(cancellationToken);
        }
        return result;
    }
}
