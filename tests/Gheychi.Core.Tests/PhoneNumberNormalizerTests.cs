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
}
