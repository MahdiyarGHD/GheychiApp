using Gheychi.Core.Services;
using Xunit;

namespace Gheychi.Core.Tests;

public sealed class SearchTextHelperTests
{
    [Fact]
    public void BuildVariants_Blank_ReturnsEmpty()
    {
        Assert.Empty(SearchTextHelper.BuildVariants(null));
        Assert.Empty(SearchTextHelper.BuildVariants("   "));
    }

    [Fact]
    public void BuildVariants_PlainText_ReturnsOnlyTrimmedQuery()
    {
        Assert.Equal(["contract"], SearchTextHelper.BuildVariants("  contract "));
    }

    [Fact]
    public void BuildVariants_ArabicLetters_AddsPersianSpelling()
    {
        var variants = SearchTextHelper.BuildVariants("علي كريم");
        Assert.Contains("علی کریم", variants);
    }

    [Fact]
    public void BuildVariants_PersianDigits_AddsAsciiDigits()
    {
        var variants = SearchTextHelper.BuildVariants("کد ۱۲۳۴");
        Assert.Contains("کد 1234", variants);
    }

    [Fact]
    public void ContainsAny_MatchesMidWordCaseInsensitive()
    {
        var variants = SearchTextHelper.BuildVariants("ELLO");
        Assert.True(SearchTextHelper.ContainsAny("Hello there", variants));
        Assert.False(SearchTextHelper.ContainsAny("Goodbye", variants));
    }

    [Fact]
    public void ContainsAny_PersianBodyFoundByArabicQuery()
    {
        var variants = SearchTextHelper.BuildVariants("علي");
        Assert.True(SearchTextHelper.ContainsAny("سلام علی جان", variants));
    }

    [Theory]
    [InlineData("Ali", "Ali", 0)]
    [InlineData("ali", "Ali Reza", 1)]
    [InlineData("reza", "Ali Reza", 2)]
    [InlineData("ali", "Khalil", 3)]
    [InlineData("ana", "Banana Ana", 2)]
    [InlineData("xyz", "Ali Reza", SearchTextHelper.NoTitleMatch)]
    public void TitleRank_BestMatchIsLowest(string query, string title, int expected)
    {
        Assert.Equal(expected, SearchTextHelper.TitleRank(title, SearchTextHelper.BuildVariants(query)));
    }

    [Fact]
    public void TitleRank_EmptyTitleDoesNotMatch()
    {
        Assert.Equal(SearchTextHelper.NoTitleMatch, SearchTextHelper.TitleRank(null, SearchTextHelper.BuildVariants("a")));
    }

    [Fact]
    public void TitleRank_PersianTitleFoundByArabicQuery()
    {
        Assert.Equal(1, SearchTextHelper.TitleRank("علی رضا", SearchTextHelper.BuildVariants("علي")));
    }
}
