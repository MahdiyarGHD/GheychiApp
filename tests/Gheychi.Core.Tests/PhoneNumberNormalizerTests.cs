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

    [Fact]
    public void ToSendAddress_UndoesFormatDisplay()
    {
        var display = PhoneNumberNormalizer.FormatDisplay("09120000001");
        Assert.Equal("+989120000001", PhoneNumberNormalizer.ToSendAddress(display));
    }

    [Theory]
    [InlineData("+98 912 123 4567", "9121234567")]
    [InlineData("09121234567", "9121234567")]
    [InlineData("۰۹۱۲۱۲۳۴۵۶۷", "9121234567")]
    [InlineData("(021) 8877-6655", "2188776655")]
    [InlineData(" +44 7911 123456 ", "7911123456")]
    public void ToContactKey_PhoneShapedNumber_MatchesTheInboxKey(string selected, string expected) =>
        Assert.Equal(expected, PhoneNumberNormalizer.ToContactKey(selected));

    [Theory]
    [InlineData("123456")]
    [InlineData("6037 9911 1111 1111")]
    [InlineData("12,500,000")]
    [InlineData("call 09121234567")]
    [InlineData("0912-123-4567 ok")]
    [InlineData("")]
    public void ToContactKey_CodesAmountsAndText_AreNoContact(string selected) =>
        Assert.Null(PhoneNumberNormalizer.ToContactKey(selected));
}
