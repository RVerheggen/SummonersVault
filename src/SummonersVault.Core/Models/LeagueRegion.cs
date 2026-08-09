namespace SummonersVault.Core.Models;

public static class LeagueRegion
{
    public static string Normalize(string? value)
    {
        var region = value?.Trim().ToUpperInvariant() ?? string.Empty;
        if (region.Length == 0) return region;

        return region switch
        {
            "EUW1" or "EUW" => "EUW",
            "EUN1" or "EUN" or "EUNE" => "EUNE",
            "NA1" or "NA" => "NA",
            "BR1" or "BR" => "BR",
            "JP1" or "JP" => "JP",
            "LA1" or "LAN" => "LAN",
            "LA2" or "LAS" => "LAS",
            "OC1" or "OC" or "OCE" => "OCE",
            "TR1" or "TR" => "TR",
            "KR1" or "KR" => "KR",
            _ => region.TrimEnd("0123456789".ToCharArray())
        };
    }
}
