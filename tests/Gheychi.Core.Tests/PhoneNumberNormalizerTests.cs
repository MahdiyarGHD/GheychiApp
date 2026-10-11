using Gheychi.Core.Services;
using Xunit;

namespace Gheychi.Core.Tests;

public class PhoneNumberNormalizerTests
{
    [Theory]
    [InlineData("+989120000001", "9120000001")]
    [InlineData("09120000001", "9120000001")]
    [InlineData("989120000001", "9120000001")]
    [InlineData("0912-000-0001", "9120000001")]
    [InlineData("IRANCELL", "IRANCELL")]
    [InlineData("بانک ملت", "بانک ملت")]
    public void ToLookupKey_ReturnsExpected(string input, string expected)
    {
        var result = PhoneNumberNormalizer.ToLookupKey(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("+989120000001", "+98 912 000 0001")]
    [InlineData("09120000001", "+98 912 000 0001")]
    [InlineData("IRANCELL", "IRANCELL")]
    public void FormatDisplay_ReturnsFormattedOrOriginal(string input, string expected)
    {
        var result = PhoneNumberNormalizer.FormatDisplay(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("+98 912 000 0001", "+989120000001")]
    [InlineData("0912-000-0001", "09120000001")]
    [InlineData("(021) 555 1234", "0215551234")]
    [InlineData("IRANCELL", "IRANCELL")]
    [InlineData("", "")]
    public void ToSendAddress_StripsSeparators(string input, string expected)
    {
        Assert.Equal(expected, PhoneNumberNormalizer.ToSendAddress(input));
    }

    // Address books in Persian often store numbers in Persian or Arabic-Indic digits; SMS addresses use ASCII.
    [Theory]
    [InlineData("۰۹۱۲۰۰۰۰۰۰۱", "9120000001")]
    [InlineData("+۹۸ ۹۱۲ ۰۰۰ ۰۰۰۱", "9120000001")]
    [InlineData("٠٩١٢٠٠٠٠٠٠١", "9120000001")]
    public void ToLookupKey_FoldsPersianAndArabicDigits(string input, string expected)
    {
        Assert.False(PhoneNumberNormalizer.IsAlphanumeric(input));
        Assert.Equal(expected, PhoneNumberNormalizer.ToLookupKey(input));
    }

    [Fact]
    public void PersianDigitNumber_MatchesAsciiAddress()
    {
        Assert.Equal(PhoneNumberNormalizer.ToLookupKey("+989120000001"), PhoneNumberNormalizer.ToLookupKey("۰۹۱۲-۰۰۰-۰۰۰۱"));
        Assert.Equal("+98 912 000 0001", PhoneNumberNormalizer.FormatDisplay("۰۹۱۲۰۰۰۰۰۰۱"));
        Assert.Equal("09120000001", PhoneNumberNormalizer.ToSendAddress("۰۹۱۲ ۰۰۰ ۰۰۰۱"));
    }

    [Theory]
    [InlineData("بانک ملت")]
    [InlineData("بانک۱")]
    [InlineData("IRANCELL")]
    public void Senders_WithLetters_StayAlphanumeric(string input)
    {
        Assert.True(PhoneNumberNormalizer.IsAlphanumeric(input));
    }

    [Fact]
    public void ToSendAddress_UndoesFormatDisplay()
    {
        var display = PhoneNumberNormalizer.FormatDisplay("09120000001");
        Assert.Equal("+989120000001", PhoneNumberNormalizer.ToSendAddress(display));
    }
}
