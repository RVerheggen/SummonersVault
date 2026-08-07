using System.Diagnostics;

namespace SummonersVault.Infrastructure.League;

internal static class LeagueInstallationLocator
{
    internal static string DefaultRiotGamesDirectory
    {
        get
        {
            var systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
            return Path.Combine(string.IsNullOrWhiteSpace(systemRoot) ? @"C:\" : systemRoot, "Riot Games");
        }
    }

    internal static IReadOnlyList<string> GetLeagueDirectories(
        string? configuredPath,
        string riotGamesDirectory,
        IEnumerable<string>? runningExecutablePaths = null)
    {
        var candidates = new List<string>();
        var configuredDirectory = GetDirectory(configuredPath);
        if (configuredDirectory is not null)
        {
            Add(candidates, configuredDirectory);
            Add(candidates, Path.Combine(configuredDirectory, "League of Legends"));
            if (IsDirectoryNamed(configuredDirectory, "Riot Client"))
                Add(candidates, Path.Combine(Path.GetDirectoryName(configuredDirectory)!, "League of Legends"));
        }

        Add(candidates, Path.Combine(riotGamesDirectory, "League of Legends"));
        foreach (var executablePath in runningExecutablePaths ?? [])
            Add(candidates, Path.GetDirectoryName(executablePath));
        return candidates;
    }

    internal static IReadOnlyList<string> GetRiotClientExecutables(
        string? configuredPath,
        string riotGamesDirectory,
        IEnumerable<string>? runningExecutablePaths = null)
    {
        var candidates = new List<string>();
        if (string.Equals(Path.GetFileName(configuredPath), "RiotClientServices.exe", StringComparison.OrdinalIgnoreCase))
            Add(candidates, configuredPath);

        var configuredDirectory = GetDirectory(configuredPath);
        if (configuredDirectory is not null)
        {
            Add(candidates, Path.Combine(configuredDirectory, "RiotClientServices.exe"));
            Add(candidates, Path.Combine(configuredDirectory, "Riot Client", "RiotClientServices.exe"));
            if (IsDirectoryNamed(configuredDirectory, "League of Legends"))
                Add(candidates, Path.Combine(Path.GetDirectoryName(configuredDirectory)!, "Riot Client", "RiotClientServices.exe"));
        }

        Add(candidates, Path.Combine(riotGamesDirectory, "Riot Client", "RiotClientServices.exe"));
        foreach (var executablePath in runningExecutablePaths ?? [])
            Add(candidates, executablePath);
        return candidates;
    }

    internal static ProcessStartInfo CreateLaunchStartInfo(string executable)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(executable)
        };
        startInfo.ArgumentList.Add("--launch-product=league_of_legends");
        startInfo.ArgumentList.Add("--launch-patchline=live");
        return startInfo;
    }

    private static string? GetDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var trimmed = path.Trim();
        return Path.GetExtension(trimmed).Equals(".exe", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(trimmed)
            : trimmed;
    }

    private static bool IsDirectoryNamed(string path, string name) =>
        string.Equals(Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), name, StringComparison.OrdinalIgnoreCase);

    private static void Add(List<string> candidates, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        string fullPath;
        try { fullPath = Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return; }
        if (!candidates.Contains(fullPath, StringComparer.OrdinalIgnoreCase)) candidates.Add(fullPath);
    }
}
