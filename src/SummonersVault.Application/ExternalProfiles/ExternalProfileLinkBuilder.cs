using System.Diagnostics.CodeAnalysis;
using SummonersVault.Core.Models;

namespace SummonersVault.Application.ExternalProfiles;

public static class ExternalProfileLinkBuilder
{
    private static readonly HashSet<string> SupportedRegions = new(StringComparer.OrdinalIgnoreCase)
    {
        "BR",
        "EUNE",
        "EUW",
        "JP",
        "KR",
        "LAN",
        "LAS",
        "NA",
        "OCE",
        "TR"
    };

    public static bool CanBuild(string? gameName, string? tagLine, string? region) =>
        TryBuild(ExternalProfileProvider.OpGg, gameName, tagLine, region, out _);

    public static bool TryBuild(
        ExternalProfileProvider provider,
        string? gameName,
        string? tagLine,
        string? region,
        [NotNullWhen(true)] out ExternalProfileLink? profileLink)
    {
        profileLink = null;
        string normalizedGameName = gameName?.Trim() ?? string.Empty;
        string normalizedTagLine = tagLine?.Trim() ?? string.Empty;
        string normalizedRegion = LeagueRegion.Normalize(region);
        if (normalizedGameName.Length == 0
            || normalizedTagLine.Length == 0
            || !SupportedRegions.Contains(normalizedRegion))
        {
            return false;
        }

        string providerName = GetProviderName(provider);
        if (providerName.Length == 0)
        {
            return false;
        }

        string regionSegment = normalizedRegion.ToLowerInvariant();
        string riotIdSegment = Uri.EscapeDataString($"{normalizedGameName}-{normalizedTagLine}");
        string address = provider switch
        {
            ExternalProfileProvider.OpGg => $"https://op.gg/lol/summoners/{regionSegment}/{riotIdSegment}",
            ExternalProfileProvider.DeepLol => $"https://www.deeplol.gg/summoner/{regionSegment}/{riotIdSegment}",
            ExternalProfileProvider.DpmLol => $"https://dpm.lol/{riotIdSegment}",
            ExternalProfileProvider.LeagueOfGraphs => $"https://www.leagueofgraphs.com/summoner/{regionSegment}/{riotIdSegment}",
            _ => string.Empty
        };

        if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? profileUri) || !IsAllowed(profileUri))
        {
            return false;
        }

        profileLink = new(provider, providerName, profileUri);
        return true;
    }

    public static bool IsAllowed(Uri? profileUri)
    {
        if (profileUri is not
            {
                IsAbsoluteUri: true,
                IsDefaultPort: true
            }
            || !profileUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || profileUri.UserInfo.Length != 0
            || profileUri.Query.Length != 0
            || profileUri.Fragment.Length != 0)
        {
            return false;
        }

        string[] segments = profileUri.GetComponents(UriComponents.Path, UriFormat.UriEscaped)
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        return profileUri.Host.ToLowerInvariant() switch
        {
            "op.gg" => segments is ["lol", "summoners", var region, var riotId]
                && IsSupportedRegionSegment(region)
                && riotId.Length > 0,
            "www.deeplol.gg" or "www.leagueofgraphs.com" => segments is ["summoner", var region, var riotId]
                && IsSupportedRegionSegment(region)
                && riotId.Length > 0,
            "dpm.lol" => segments is [var riotId] && riotId.Length > 0,
            _ => false
        };
    }

    public static string GetProviderName(ExternalProfileProvider provider) => provider switch
    {
        ExternalProfileProvider.OpGg => "OP.GG",
        ExternalProfileProvider.DeepLol => "DeepLoL",
        ExternalProfileProvider.DpmLol => "DPM.LOL",
        ExternalProfileProvider.LeagueOfGraphs => "LeagueOfGraphs",
        _ => string.Empty
    };

    private static bool IsSupportedRegionSegment(string region) => SupportedRegions.Contains(region);
}
