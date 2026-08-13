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
        string searchText = query?.Trim() ?? string.Empty;
        return [.. source
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(value => Create(value, searchText))
            .Where(item => item is not null)
            .Cast<SearchSuggestionItem>()
            .OrderBy(item => item.MatchKind)
            .ThenBy(item => item.Value, StringComparer.OrdinalIgnoreCase)];
    }

    private static SearchSuggestionItem? Create(string value, string searchText)
    {
        if (searchText.Length == 0)
        {
            return new(value, SearchSuggestionMatchKind.Contains, [new(value, false)]);
        }

        int firstMatch = value.IndexOf(searchText, StringComparison.OrdinalIgnoreCase);
        if (firstMatch < 0)
        {
            return null;
        }

        SearchSuggestionMatchKind kind = value.Equals(searchText, StringComparison.OrdinalIgnoreCase)
            ? SearchSuggestionMatchKind.Exact
            : firstMatch == 0
                ? SearchSuggestionMatchKind.Prefix
                : SearchSuggestionMatchKind.Contains;

        return new(value, kind, SplitIntoSegments(value, searchText));
    }

    private static List<SearchSuggestionSegment> SplitIntoSegments(string value, string searchText)
    {
        var segments = new List<SearchSuggestionSegment>();
        int position = 0;
        while (position < value.Length)
        {
            int match = value.IndexOf(searchText, position, StringComparison.OrdinalIgnoreCase);
            if (match < 0)
            {
                segments.Add(new(value[position..], false));
                break;
            }

            if (match > position)
            {
                segments.Add(new(value[position..match], false));
            }

            segments.Add(new(value.Substring(match, searchText.Length), true));
            position = match + searchText.Length;
        }

        return segments;
    }
}
