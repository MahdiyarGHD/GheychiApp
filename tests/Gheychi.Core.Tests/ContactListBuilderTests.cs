using Gheychi.Core.Models;
using Gheychi.Core.Services;
using Xunit;

namespace Gheychi.Core.Tests;

public class ContactListBuilderTests
{
    private static readonly ContactEntry[] Sample =
    [
        new("Sara Mansour", "+1 (555) 671-8200", "Mobile"),
        new("Arash K.", "0912 000 0002", "Mobile"),
        new("arman", "09121234567", "Home"),
        new("علی رضایی", "09351112233", "Mobile"),
        new("1st Pizza", "02188776655", "Work"),
    ];

    [Theory]
    [InlineData("Arash", "A")]
    [InlineData("  sara", "S")]
    [InlineData("علی", "ع")]
    [InlineData("1st Pizza", "#")]
    [InlineData("", "#")]
    [InlineData(null, "#")]
    public void SectionLetter_UsesFirstLetterOrHash(string? name, string expected) =>
        Assert.Equal(expected, ContactListBuilder.SectionLetter(name));

    [Fact]
    public void Build_GroupsSortedWithHashLast()
    {
        var sections = ContactListBuilder.Build(Sample, null);

        Assert.Equal(["A", "S", "ع", "#"], sections.Select(s => s.Letter));
        Assert.Equal(["Arash K.", "arman"], sections[0].Contacts.Select(c => c.Name));
    }

    [Fact]
    public void Build_FiltersByNameIgnoringCase()
    {
        var sections = ContactListBuilder.Build(Sample, "ARA");

        Assert.Equal(["Arash K.", "Sara Mansour"], sections.SelectMany(s => s.Contacts).Select(c => c.Name));
    }

    [Fact]
    public void Build_FiltersByNumberDigitsIgnoringFormatting()
    {
        var sections = ContactListBuilder.Build(Sample, "0912000");

        Assert.Equal(["Arash K."], sections.SelectMany(s => s.Contacts).Select(c => c.Name));
    }

    [Fact]
    public void Build_FindsPersianDigitQuery()
    {
        var sections = ContactListBuilder.Build(Sample, "۰۹۳۵۱");

        Assert.Equal(["علی رضایی"], sections.SelectMany(s => s.Contacts).Select(c => c.Name));
    }

    [Fact]
    public void Build_NoMatch_IsEmpty() =>
        Assert.Empty(ContactListBuilder.Build(Sample, "zzz"));

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
}
