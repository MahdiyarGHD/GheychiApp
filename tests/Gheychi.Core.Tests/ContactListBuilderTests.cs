using Gheychi.Core.Services;
using Xunit;

namespace Gheychi.Core.Tests;

public class ContactListBuilderTests
{
    [Theory]
    [InlineData("Arash", "A")]
    [InlineData("  sara", "S")]
    [InlineData("علی", "ع")]
    [InlineData("1st Pizza", "#")]
    [InlineData("", "#")]
    [InlineData(null, "#")]
    public void SectionLetter_UsesFirstLetterOrHash(string? name, string expected) =>
        Assert.Equal(expected, ContactListBuilder.SectionLetter(name));

    [Theory]
    [InlineData("0912 000-0002", "09120000002")]
    [InlineData("+98 (912) 000 0002", "+989120000002")]
    [InlineData("۰۹۱۲۱۲۳۴۵۶۷", "09121234567")]
    [InlineData("3000", "3000")]
    [InlineData("  +1.555.2938411 ", "+15552938411")]
    public void TypedAddress_AcceptsNumbers(string text, string expected) =>
        Assert.Equal(expected, ContactListBuilder.TypedAddress(text));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Arash")]
    [InlineData("0903 abc")]
    [InlineData("+")]
    [InlineData("09+03")]
    public void TypedAddress_RejectsNames(string? text) =>
        Assert.Equal(string.Empty, ContactListBuilder.TypedAddress(text));

    [Theory]
    [InlineData("+989120000002", true)]
    [InlineData("0912 000 0002", true)]
    [InlineData("1000", false)]
    [InlineData("300012", false)]
    [InlineData("Bank Mellat", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsPersonalNumber_SeparatesPeopleFromShortcodesAndNames(string? address, bool expected) =>
        Assert.Equal(expected, ContactListBuilder.IsPersonalNumber(address));
}
