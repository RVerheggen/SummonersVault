using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;

namespace SummonersVault.Infrastructure.League;

public sealed class LeagueClientConnection
{
    private string? _configuredInstallDirectory;

    public void SetInstallDirectory(string? directory) => _configuredInstallDirectory = directory;

    internal async Task<LeagueLockfile?> FindLockfileAsync(CancellationToken cancellationToken)
    {
        foreach (string directory in CandidateLeagueDirectories())
        {
            string path = Path.Combine(directory, "lockfile");
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                string content = await ReadLockfileAsync(path, cancellationToken).ConfigureAwait(false);
                if (LeagueLockfile.TryParse(content, out LeagueLockfile? lockfile))
                {
                    return lockfile;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return null;
    }

    internal static HttpClient CreateAuthenticatedClient(LeagueLockfile lockfile)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (request, _, _, _) => IsLeagueLoopbackRequest(request.RequestUri)
        };
        var client = new HttpClient(handler) { BaseAddress = lockfile.BaseUri, Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"riot:{lockfile.Password}")));
        return client;
    }

    public Task<bool> LaunchAsync(string? configuredInstallDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _configuredInstallDirectory = configuredInstallDirectory ?? _configuredInstallDirectory;
        string? executable = CandidateRiotClientExecutables().FirstOrDefault(File.Exists);
        if (executable is null)
        {
            return Task.FromResult(false);
        }

        try
        {
            Process.Start(LeagueInstallationLocator.CreateLaunchStartInfo(executable));
            return Task.FromResult(true);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return Task.FromResult(false);
        }
    }

    internal static async Task<string> ReadLockfileAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 256, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static bool IsLeagueLoopbackRequest(Uri? uri) =>
        uri is { IsLoopback: true, Host: "127.0.0.1", Scheme: "https" };

    private IReadOnlyList<string> CandidateLeagueDirectories() => LeagueInstallationLocator.GetLeagueDirectories(
        _configuredInstallDirectory,
        LeagueInstallationLocator.DefaultRiotGamesDirectory,
        GetRunningExecutablePaths("LeagueClient", "LeagueClientUx"));

    private IReadOnlyList<string> CandidateRiotClientExecutables() => LeagueInstallationLocator.GetRiotClientExecutables(
        _configuredInstallDirectory,
        LeagueInstallationLocator.DefaultRiotGamesDirectory,
        GetRunningExecutablePaths("RiotClientServices"));

    private static IEnumerable<string> GetRunningExecutablePaths(params string[] processNames)
    {
        foreach (string processName in processNames)
        {
            Process[] processes = [];
            try { processes = Process.GetProcessesByName(processName); }
            catch (InvalidOperationException) { }

            foreach (Process process in processes)
            {
                using (process)
                {
                    string? path = null;
                    try { path = process.MainModule?.FileName; }
                    catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or NotSupportedException) { }
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        yield return path;
                    }
                }
            }
        }
    }
}
