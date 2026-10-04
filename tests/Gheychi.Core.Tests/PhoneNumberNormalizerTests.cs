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
}
