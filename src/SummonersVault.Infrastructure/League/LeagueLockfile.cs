namespace SummonersVault.Infrastructure.League;

public sealed record LeagueLockfile(string ProcessName, int ProcessId, int Port, string Password, string Protocol)
{
    public Uri BaseUri => new($"https://127.0.0.1:{Port}/", UriKind.Absolute);

    public static bool TryParse(string? value, out LeagueLockfile? lockfile)
    {
        lockfile = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string[] parts = value.Trim().Split(':');
        if (parts.Length != 5 || !int.TryParse(parts[1], out int processId) || !int.TryParse(parts[2], out int port) || port is < 1 or > 65535 || parts[3].Length < 1 || parts[4] is not ("https" or "http"))
        {
            return false;
        }

        lockfile = new(parts[0], processId, port, parts[3], parts[4]);
        return true;
    }
}

