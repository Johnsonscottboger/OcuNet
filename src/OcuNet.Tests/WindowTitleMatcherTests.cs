using System;
using Xunit;

namespace OcuNet.Tests;

public class WindowTitleMatcherTests
{
    private static WindowInfo Win(string title)
    {
        return new WindowInfo(IntPtr.Zero, title, new Rectangle(0, 0, 100, 100));
    }

    [Fact]
    public void Substring_Match_IsCaseInsensitive()
    {
        var windows = new[] { Win("MyAgent Test App"), Win("Calculator") };

        var matches = WindowTitleMatcher.MatchAll(windows, "myagent", WindowTitleMatchMode.Substring, fuzzyThreshold: 0.6);

        Assert.Single(matches);
        Assert.Equal("MyAgent Test App", matches[0].Title);
    }

    [Fact]
    public void Substring_NoMatch_ReturnsEmpty()
    {
        var windows = new[] { Win("Calculator") };

        var matches = WindowTitleMatcher.MatchAll(windows, "not-there", WindowTitleMatchMode.Substring, fuzzyThreshold: 0.6);

        Assert.Empty(matches);
    }

    [Fact]
    public void Substring_MultipleMatches_ReturnsAll()
    {
        var windows = new[] { Win("Calc"), Win("Calculator"), Win("Notepad") };

        var matches = WindowTitleMatcher.MatchAll(windows, "calc", WindowTitleMatchMode.Substring, fuzzyThreshold: 0.6);

        Assert.Equal(2, matches.Count);
    }

    [Fact]
    public void Regex_Match_UsesIgnoreCase()
    {
        var windows = new[] { Win("MyAgent Test App"), Win("Calculator") };

        var matches = WindowTitleMatcher.MatchAll(windows, "^myagent.*app$", WindowTitleMatchMode.Regex, fuzzyThreshold: 0.6);

        Assert.Single(matches);
        Assert.Equal("MyAgent Test App", matches[0].Title);
    }

    [Fact]
    public void Regex_NoMatch_ReturnsEmpty()
    {
        var windows = new[] { Win("Calculator") };

        var matches = WindowTitleMatcher.MatchAll(windows, "^myagent", WindowTitleMatchMode.Regex, fuzzyThreshold: 0.6);

        Assert.Empty(matches);
    }

    [Fact]
    public void TryCompileRegex_InvalidPattern_ReturnsFalse()
    {
        Assert.False(WindowTitleMatcher.TryCompileRegex("[", out _));
        Assert.True(WindowTitleMatcher.TryCompileRegex("^valid$", out _));
    }

    [Fact]
    public void Fuzzy_PicksBestMatchWithinThreshold()
    {
        var windows = new[] { Win("Calculator"), Win("Calcuator"), Win("Notepad") };

        var matches = WindowTitleMatcher.MatchAll(windows, "Calculator", WindowTitleMatchMode.Fuzzy, fuzzyThreshold: 0.6);

        Assert.Single(matches);
        Assert.Equal("Calculator", matches[0].Title);
    }

    [Fact]
    public void Fuzzy_TypoMatchesBestCandidate()
    {
        var windows = new[] { Win("Calculator"), Win("Notepad") };

        var matches = WindowTitleMatcher.MatchAll(windows, "Calcuator", WindowTitleMatchMode.Fuzzy, fuzzyThreshold: 0.6);

        Assert.Single(matches);
        Assert.Equal("Calculator", matches[0].Title);
    }

    [Fact]
    public void Fuzzy_BelowThreshold_ReturnsEmpty()
    {
        var windows = new[] { Win("Notepad") };

        var matches = WindowTitleMatcher.MatchAll(windows, "Calculator", WindowTitleMatchMode.Fuzzy, fuzzyThreshold: 0.6);

        Assert.Empty(matches);
    }

    [Fact]
    public void Fuzzy_HighThreshold_RejectsTypo()
    {
        var windows = new[] { Win("Calculator") };

        var matches = WindowTitleMatcher.MatchAll(windows, "Calcuator", WindowTitleMatchMode.Fuzzy, fuzzyThreshold: 0.95);

        Assert.Empty(matches);
    }

    [Fact]
    public void Similarity_IdenticalStrings_IsOne()
    {
        Assert.Equal(1, WindowTitleMatcher.GetSimilarity("Hello World", "Hello World"));
    }

    [Fact]
    public void Similarity_NormalizesCaseAndWhitespace()
    {
        Assert.Equal(1, WindowTitleMatcher.GetSimilarity("  Hello   World ", "hello world"));
    }

    [Fact]
    public void Similarity_CompletelyDifferent_IsZeroOrLow()
    {
        Assert.True(WindowTitleMatcher.GetSimilarity("Calculator", "Notepad") < 0.3);
    }

    [Fact]
    public void Similarity_ChineseTypo_IsHigh()
    {
        Assert.True(WindowTitleMatcher.GetSimilarity("财联社首页", "财联社") >= 0.6);
    }
}
