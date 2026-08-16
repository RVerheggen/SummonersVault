namespace SummonersVault.Core.Models;

public static class LeagueRegion
{
    private static readonly char[] PlatformSuffixDigits = "0123456789".ToCharArray();

    public const string Unknown = "UNKNOWN";
    public const string EuropeWest = "EUW";
    public const string EuropeNordicAndEast = "EUNE";
    public const string NorthAmerica = "NA";
    public const string Korea = "KR";
    public const string Brazil = "BR";
    public const string Japan = "JP";
    public const string LatinAmericaNorth = "LAN";
    public const string LatinAmericaSouth = "LAS";
    public const string Oceania = "OCE";
    public const string Turkey = "TR";

    public static IReadOnlyList<string> Supported { get; } = Array.AsReadOnly<string>(
    [
        EuropeWest,
        EuropeNordicAndEast,
        NorthAmerica,
        Korea,
        Brazil,
        Japan,
        LatinAmericaNorth,
        LatinAmericaSouth,
        Oceania,
        Turkey
    ]);

    public static bool IsSupported(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && Supported.Contains(value, StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string? value)
    {
        string region = value?.Trim().ToUpperInvariant() ?? string.Empty;
        if (region.Length == 0)
        {
            return region;
        }

        return region switch
        {
            "EUW1" or EuropeWest => EuropeWest,
            "EUN1" or "EUN" or EuropeNordicAndEast => EuropeNordicAndEast,
            "NA1" or NorthAmerica => NorthAmerica,
            "BR1" or Brazil => Brazil,
            "JP1" or Japan => Japan,
            "LA1" or LatinAmericaNorth => LatinAmericaNorth,
            "LA2" or LatinAmericaSouth => LatinAmericaSouth,
            "OC1" or "OC" or Oceania => Oceania,
            "TR1" or Turkey => Turkey,
            "KR1" or Korea => Korea,
            _ => region.TrimEnd(PlatformSuffixDigits)
        };
    }
}
