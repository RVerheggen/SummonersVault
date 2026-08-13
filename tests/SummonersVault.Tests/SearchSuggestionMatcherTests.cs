using SummonersVault.Core.Services;
using Xunit;

namespace SummonersVault.Tests;

public sealed class SearchSuggestionMatcherTests
{
    [Fact]
    public void Match_PrioritizesPrefixMatchesForTheReExample()
    {
        IReadOnlyList<SearchSuggestionItem> result = SearchSuggestionMatcher.Match(
            ["Ezreal", "Aurelion Sol", "Rell", "Garen", "Rek'Sai", "Ahri"],
            "Re");

        Assert.Equal(
            ["Rek'Sai", "Rell", "Aurelion Sol", "Ezreal", "Garen"],
            result.Select(item => item.Value));
        Assert.Equal(SearchSuggestionMatchKind.Prefix, result[0].MatchKind);
        Assert.Equal(SearchSuggestionMatchKind.Contains, result[2].MatchKind);
    }

    [Fact]
    public void Match_PrioritizesAnExactMatchBeforeOtherPrefixes()
    {
        IReadOnlyList<SearchSuggestionItem> result = SearchSuggestionMatcher.Match(["Renata Glasc", "re"], "Re");

        Assert.Equal(["re", "Renata Glasc"], result.Select(item => item.Value));
        Assert.Equal(SearchSuggestionMatchKind.Exact, result[0].MatchKind);
    }

    [Fact]
    public void Match_FindsChampionNameInsideSkinNameAndExcludesUnrelatedSkins()
    {
        IReadOnlyList<SearchSuggestionItem> result = SearchSuggestionMatcher.Match(
            ["Brolaf", "Butcher Olaf", "Forsaken Olaf", "PROJECT: Warwick"],
            "oLaF");

        Assert.Equal(["Brolaf", "Butcher Olaf", "Forsaken Olaf"], result.Select(item => item.Value));
    }

    [Fact]
    public void Match_HighlightsEveryOccurrenceAndPreservesOriginalCasing()
    {
        SearchSuggestionItem item = Assert.Single(SearchSuggestionMatcher.Match(["ReRE"], "re"));

        Assert.Equal(["Re", "RE"], item.Segments.Select(segment => segment.Text));
        Assert.All(item.Segments, segment => Assert.True(segment.IsMatch));
    }

    [Fact]
    public void Match_EmptyQueryReturnsDistinctAlphabeticalOptionsWithoutHighlights()
    {
        IReadOnlyList<SearchSuggestionItem> result = SearchSuggestionMatcher.Match(["Zed", "Ahri", "zed", "  "], "  ");

        Assert.Equal(["Ahri", "Zed"], result.Select(item => item.Value));
        Assert.All(result.SelectMany(item => item.Segments), segment => Assert.False(segment.IsMatch));
    }

    [Fact]
    public void Match_NoMatchReturnsNoOptions()
    {
        Assert.Empty(SearchSuggestionMatcher.Match(["Ahri", "Zed"], "Olaf"));
    }
}
