namespace SummonersVault.Core.Services;

internal enum SearchSuggestionMatchKind
{
    Exact,
    Prefix,
    Contains
}

internal sealed record SearchSuggestionSegment(string Text, bool IsMatch);

internal sealed record SearchSuggestionItem(
    string Value,
    SearchSuggestionMatchKind MatchKind,
    IReadOnlyList<SearchSuggestionSegment> Segments);

internal static class SearchSuggestionMatcher
{
    public static IReadOnlyList<SearchSuggestionItem> Match(IEnumerable<string> source, string? query)
    {
        var searchText = query?.Trim() ?? string.Empty;
        return source
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Select(value => Create(value, searchText))
            .Where(item => item is not null)
            .Cast<SearchSuggestionItem>()
            .OrderBy(item => item.MatchKind)
            .ThenBy(item => item.Value, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static SearchSuggestionItem? Create(string value, string searchText)
    {
        if (searchText.Length == 0)
            return new(value, SearchSuggestionMatchKind.Contains, [new(value, false)]);

        var firstMatch = value.IndexOf(searchText, StringComparison.CurrentCultureIgnoreCase);
        if (firstMatch < 0) return null;

        var kind = value.Equals(searchText, StringComparison.CurrentCultureIgnoreCase)
            ? SearchSuggestionMatchKind.Exact
            : firstMatch == 0
                ? SearchSuggestionMatchKind.Prefix
                : SearchSuggestionMatchKind.Contains;

        return new(value, kind, SplitIntoSegments(value, searchText));
    }

    private static IReadOnlyList<SearchSuggestionSegment> SplitIntoSegments(string value, string searchText)
    {
        var segments = new List<SearchSuggestionSegment>();
        var position = 0;
        while (position < value.Length)
        {
            var match = value.IndexOf(searchText, position, StringComparison.CurrentCultureIgnoreCase);
            if (match < 0)
            {
                segments.Add(new(value[position..], false));
                break;
            }

            if (match > position) segments.Add(new(value[position..match], false));
            segments.Add(new(value.Substring(match, searchText.Length), true));
            position = match + searchText.Length;
        }

        return segments;
    }
}
