using SummonersVault.Core.Models;

namespace SummonersVault.Core.Services;

public enum AccountSort { Name, RecentlyPlayed }
public sealed record AccountFilter(string? Region = null, string? RankOrQueue = null, string? Role = null, string? Champion = null, string? Skin = null, MatchHistoryState? SyncState = null);

public static class AccountSearch
{
    public static IEnumerable<VaultAccount> Apply(IEnumerable<VaultAccount> source, string? query, AccountSort sort, AccountFilter? facets = null)
    {
        var words = (query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var filtered = source.Where(account => words.All(word => Matches(account, word)) && MatchesFacets(account, facets));
        return sort == AccountSort.RecentlyPlayed
            ? filtered.OrderByDescending(x => x.LastMatchPlayedAtUtc.HasValue).ThenByDescending(x => x.LastMatchPlayedAtUtc).ThenBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            : filtered.OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase);
    }

    private static bool MatchesFacets(VaultAccount account, AccountFilter? filter)
    {
        if (filter is null) return true;
        var comparison = StringComparison.CurrentCultureIgnoreCase;
        return (string.IsNullOrWhiteSpace(filter.Region) || account.Region.Contains(filter.Region, comparison))
            && (string.IsNullOrWhiteSpace(filter.RankOrQueue) || account.Ranks.Any(x => x.Tier.Contains(filter.RankOrQueue, comparison) || x.QueueType.Contains(filter.RankOrQueue, comparison)))
            && (string.IsNullOrWhiteSpace(filter.Role) || account.Roles.ToString().Contains(filter.Role, comparison))
            && (string.IsNullOrWhiteSpace(filter.Champion) || account.Champions.Any(x => x.Name.Contains(filter.Champion, comparison)))
            && (string.IsNullOrWhiteSpace(filter.Skin) || account.Skins.Any(x => x.Name.Contains(filter.Skin, comparison)))
            && (!filter.SyncState.HasValue || account.MatchHistoryState == filter.SyncState.Value);
    }

    private static bool Matches(VaultAccount account, string term)
    {
        var comparison = StringComparison.CurrentCultureIgnoreCase;
        return account.DisplayName.Contains(term, comparison)
            || account.LoginIdentifier.Contains(term, comparison)
            || account.Region.Contains(term, comparison)
            || (account.Notes?.Contains(term, comparison) ?? false)
            || account.Roles.ToString().Contains(term, comparison)
            || account.Ranks.Any(x => x.Tier.Contains(term, comparison) || x.Division.Contains(term, comparison) || x.QueueType.Contains(term, comparison))
            || account.Champions.Any(x => x.Name.Contains(term, comparison))
            || account.Skins.Any(x => x.Name.Contains(term, comparison));
    }
}
