using Gheychi.Core.Services;
using Xunit;

namespace Gheychi.Core.Tests;

public sealed class SmsSegmentCounterTests
{
    private const string Persian = "س";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n ")]
    public void Empty_IsOneEmptyGsmPart(string? text)
    {
        Assert.Equal(new SmsSegments(1, 160, SmsEncoding.Gsm7), SmsSegmentCounter.Count(text));
    }

    [Fact]
    public void ShortGsm_CountsRemainingOfOnePart()
    {
        Assert.Equal(new SmsSegments(1, 155, SmsEncoding.Gsm7), SmsSegmentCounter.Count("Hello"));
    }

    [Fact]
    public void Gsm_Exactly160_IsOnePartWithNothingLeft()
    {
        Assert.Equal(new SmsSegments(1, 0, SmsEncoding.Gsm7), SmsSegmentCounter.Count(new string('a', 160)));
    }

    [Fact]
    public void Gsm_161_SplitsIntoTwoPartsOf153()
    {
        Assert.Equal(new SmsSegments(2, 145, SmsEncoding.Gsm7), SmsSegmentCounter.Count(new string('a', 161)));
    }

    [Theory]
    [InlineData(153 * 2, 2, 0)]
    [InlineData(153 * 2 + 1, 3, 152)]
    [InlineData(153 * 3, 3, 0)]
    public void Gsm_MultipartBoundaries(int length, int parts, int remaining)
    {
        Assert.Equal(new SmsSegments(parts, remaining, SmsEncoding.Gsm7), SmsSegmentCounter.Count(new string('x', length)));
    }

    [Theory]
    [InlineData("^")]
    [InlineData("{")]
    [InlineData("}")]
    [InlineData("\\")]
    [InlineData("[")]
    [InlineData("~")]
    [InlineData("]")]
    [InlineData("|")]
    [InlineData("€")]
    [InlineData("\f")]
    public void GsmExtensionCharacters_TakeTwo(string ch)
    {
        // Wrapped in letters because a lone form feed is trimmed away as white space.
        Assert.Equal(new SmsSegments(1, 160 - 4, SmsEncoding.Gsm7), SmsSegmentCounter.Count("a" + ch + "b"));
    }

    [Fact]
    public void GsmExtensionCharacters_PushASingleMessageOver160()
    {
        // 80 euro signs are 160 septets; one more letter makes it 161.
        Assert.Equal(new SmsSegments(1, 0, SmsEncoding.Gsm7), SmsSegmentCounter.Count(new string('€', 80)));
        Assert.Equal(2, SmsSegmentCounter.Count(new string('€', 80) + "a").Parts);
    }

    [Fact]
    public void GsmExtensionCharacter_IsNotCutAcrossTwoParts()
    {
        // 152 letters then a euro sign: the euro would end at septet 154, so it starts the second part.
        var text = new string('a', 152) + "€" + new string('b', 9);
        var result = SmsSegmentCounter.Count(text);

        Assert.Equal(2, result.Parts);
        Assert.Equal(153 - 11, result.Remaining);
    }

    [Fact]
    public void Gsm_AccentedLettersOfTheDefaultAlphabetStayGsm()
    {
        var result = SmsSegmentCounter.Count("£$¥èéùìòÇØøÅåΔΦΓΛΩΠΨΣΘΞÆæßÉ¤¡ÄÖÑÜ§¿äöñüà");

        Assert.Equal(SmsEncoding.Gsm7, result.Encoding);
    }

    [Fact]
    public void Newline_IsOneSeptetInsideTheText_AndTrimmedAtTheEnds()
    {
        Assert.Equal(new SmsSegments(1, 160 - 3, SmsEncoding.Gsm7), SmsSegmentCounter.Count("a\nb"));
        Assert.Equal(new SmsSegments(1, 160 - 3, SmsEncoding.Gsm7), SmsSegmentCounter.Count("\n a\nb \n"));
        Assert.Equal(new SmsSegments(1, 160 - 4, SmsEncoding.Gsm7), SmsSegmentCounter.Count("a\r\nb"));
    }

    [Fact]
    public void Persian_ShortTextIsUcs2()
    {
        Assert.Equal(new SmsSegments(1, 70 - 5, SmsEncoding.Ucs2), SmsSegmentCounter.Count("سلام!"));
    }

    [Fact]
    public void Persian_Exactly70_IsOnePart()
    {
        Assert.Equal(new SmsSegments(1, 0, SmsEncoding.Ucs2), SmsSegmentCounter.Count(Repeat(Persian, 70)));
    }

    [Fact]
    public void Persian_71_SplitsIntoTwoPartsOf67()
    {
        Assert.Equal(new SmsSegments(2, 134 - 71, SmsEncoding.Ucs2), SmsSegmentCounter.Count(Repeat(Persian, 71)));
    }

    [Theory]
    [InlineData(134, 2, 0)]
    [InlineData(135, 3, 66)]
    [InlineData(201, 3, 0)]
    public void Persian_MultipartBoundaries(int length, int parts, int remaining)
    {
        Assert.Equal(new SmsSegments(parts, remaining, SmsEncoding.Ucs2), SmsSegmentCounter.Count(Repeat(Persian, length)));
    }

    [Fact]
    public void OneNonGsmCharacter_TurnsTheWholeTextUcs2()
    {
        // 100 plain letters would be one GSM part; a single Persian letter makes them 101 units, two parts.
        var result = SmsSegmentCounter.Count(new string('a', 100) + Persian);

        Assert.Equal(new SmsSegments(2, 134 - 101, SmsEncoding.Ucs2), result);
    }

    [Fact]
    public void Ucs2_CountsExtensionCharactersAsOneUnit()
    {
        // In UCS-2 the euro sign is one unit, not two.
        Assert.Equal(new SmsSegments(1, 70 - 4, SmsEncoding.Ucs2), SmsSegmentCounter.Count(Persian + "€" + "a" + "^"));
    }

    [Fact]
    public void Emoji_IsTwoUtf16UnitsAndForcesUcs2()
    {
        Assert.Equal(new SmsSegments(1, 70 - 2, SmsEncoding.Ucs2), SmsSegmentCounter.Count("😀"));
        Assert.Equal(new SmsSegments(1, 70 - 7, SmsEncoding.Ucs2), SmsSegmentCounter.Count("hi 😀😀"));
    }

    [Fact]
    public void Emoji_PushesLongEnglishTextToUcs2Limits()
    {
        var result = SmsSegmentCounter.Count(new string('a', 69) + "😀");

        Assert.Equal(new SmsSegments(2, 134 - 71, SmsEncoding.Ucs2), result);
    }

    [Fact]
    public void Persian_DigitsAndEnglishDigitsDifferInEncoding()
    {
        Assert.Equal(SmsEncoding.Ucs2, SmsSegmentCounter.Count("۱۲۳").Encoding);
        Assert.Equal(SmsEncoding.Gsm7, SmsSegmentCounter.Count("123").Encoding);
    }

    [Fact]
    public void Span_AndStringOverloads_Agree()
    {
        const string text = "Total: 5€ — سلام";

        Assert.Equal(SmsSegmentCounter.Count(text), SmsSegmentCounter.Count(text.AsSpan()));
    }

    private static string Repeat(string unit, int times) => string.Concat(Enumerable.Repeat(unit, times));
}
