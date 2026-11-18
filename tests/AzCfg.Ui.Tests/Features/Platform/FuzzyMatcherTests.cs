using System.Diagnostics;
using AzCfg.Ui.Features.Platform;

namespace AzCfg.Ui.Tests.Features.Platform;

public sealed class FuzzyMatcherTests
{
    [Fact]
    public void Score_QueryNotInOrder_IsNull() => Assert.Null(FuzzyMatcher.Score("xyz", "Toggle sidebar"));

    [Fact]
    public void Score_EmptyQuery_MatchesEverything() => Assert.Equal(0, FuzzyMatcher.Score("", "anything"));

    [Fact]
    public void Score_IsCaseInsensitive() => Assert.NotNull(FuzzyMatcher.Score("TOGGLE", "toggle sidebar"));

    [Fact]
    public void Rank_PrefixBeatsSubstringBeatsSubsequence()
    {
        var ranked = FuzzyMatcher.Rank(["Close tab", "Toggle sidebar", "Show tab strip", "Tab list"], "tab", s => s);
        Assert.Equal(["Tab list", "Close tab", "Show tab strip"], ranked);
    }

    [Fact]
    public void Rank_WordInitials_FindsSubsequence()
    {
        var ranked = FuzzyMatcher.Rank(["Close tab", "Toggle sidebar", "Next tab"], "ts", s => s);
        Assert.Equal("Toggle sidebar", ranked[0]);
    }

    [Fact]
    public void Rank_EqualScores_KeepOriginalOrder()
    {
        var ranked = FuzzyMatcher.Rank(["a", "b", "c"], "", s => s);
        Assert.Equal(["a", "b", "c"], ranked);
    }

    [Fact]
    public void Rank_NonMatches_AreDropped() =>
        Assert.Equal(["Alpha"], FuzzyMatcher.Rank(["Alpha", "Beta"], "alp", s => s));

    [Fact]
    public void Rank_2000Items_IsFast()
    {
        var items = Enumerable.Range(0, 2000).Select(i => $"Section{i / 50}:Group{i % 7}:Key{i}").ToList();
        FuzzyMatcher.Rank(items, "sgk", s => s);
        var stopwatch = Stopwatch.StartNew();
        var ranked = FuzzyMatcher.Rank(items, "sgk1", s => s);
        stopwatch.Stop();
        Assert.NotEmpty(ranked);
        Assert.True(stopwatch.ElapsedMilliseconds < 200, $"took {stopwatch.ElapsedMilliseconds} ms");
    }
}
