using SummonersVault.Core.Models;

namespace SummonersVault.Core.Services;

public enum AccountSort { Name, RecentlyPlayed }
public sealed record AccountFilter(string? Region = null, string? Queue = null, string? Rank = null, AccountRole Roles = AccountRole.None, string? Champion = null, string? Skin = null, MatchHistoryState? SyncState = null);

public static class AccountSearch
{
    public static IEnumerable<VaultAccount> Apply(IEnumerable<VaultAccount> source, string? query, AccountSort sort, AccountFilter? facets = null)
    {
        string[] words = (query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        IEnumerable<VaultAccount> filtered = source.Where(account => words.All(word => Matches(account, word)) && MatchesFacets(account, facets));
        return sort == AccountSort.RecentlyPlayed
            ? filtered.OrderByDescending(x => x.LastMatchPlayedAtUtc.HasValue).ThenByDescending(x => x.LastMatchPlayedAtUtc).ThenBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            : filtered.OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase);
    }

    private static bool MatchesFacets(VaultAccount account, AccountFilter? filter)
    {
        if (filter is null)
        {
            return true;
        }

        StringComparison comparison = StringComparison.CurrentCultureIgnoreCase;
        string regionFilter = LeagueRegion.Normalize(filter.Region);
        string? rankFilter = filter.Rank?.Trim();
        string? championFilter = filter.Champion?.Trim();
        string? skinFilter = filter.Skin?.Trim();
        bool rankMatches;
        if (string.IsNullOrWhiteSpace(filter.Queue) && string.IsNullOrWhiteSpace(rankFilter))
        {
            rankMatches = true;
        }
        else if (string.Equals(rankFilter, "Unranked", comparison))
        {
            rankMatches = account.Ranks.Any(rank => (string.IsNullOrWhiteSpace(filter.Queue) || rank.QueueType.Equals(filter.Queue, comparison)) && rank.Tier.Equals("UNRANKED", comparison))
                || string.IsNullOrWhiteSpace(filter.Queue) && account.Ranks.Count == 0;
        }
        else
        {
            rankMatches = account.Ranks.Any(rank =>
                (string.IsNullOrWhiteSpace(filter.Queue) || rank.QueueType.Equals(filter.Queue, comparison))
                && (string.IsNullOrWhiteSpace(rankFilter) || $"{rank.Tier} {rank.Division}".Contains(rankFilter, comparison)));
        }

        return (string.IsNullOrWhiteSpace(regionFilter) || LeagueRegion.Normalize(account.Region).Equals(regionFilter, comparison))
            && rankMatches
            && (filter.Roles == AccountRole.None || (account.Roles & filter.Roles) != AccountRole.None)
            && (string.IsNullOrWhiteSpace(championFilter) || account.Champions.Any(x => x.Name.Contains(championFilter, comparison)))
            && (string.IsNullOrWhiteSpace(skinFilter) || OwnedSkinRules.Normalize(account.Skins).Any(x => x.Name.Contains(skinFilter, comparison)))
            && (!filter.SyncState.HasValue || account.MatchHistoryState == filter.SyncState.Value);
    }

    private static bool Matches(VaultAccount account, string term)
    {
        StringComparison comparison = StringComparison.CurrentCultureIgnoreCase;
        return account.DisplayName.Contains(term, comparison)
            || account.Username.Contains(term, comparison)
            || account.Region.Contains(term, comparison)
            || (account.Notes?.Contains(term, comparison) ?? false)
            || account.Roles.ToString().Contains(term, comparison)
            || account.Ranks.Any(x => x.Tier.Contains(term, comparison) || x.Division.Contains(term, comparison) || x.QueueType.Contains(term, comparison))
            || account.Champions.Any(x => x.Name.Contains(term, comparison))
            || OwnedSkinRules.Normalize(account.Skins).Any(x => x.Name.Contains(term, comparison));
    }
}
