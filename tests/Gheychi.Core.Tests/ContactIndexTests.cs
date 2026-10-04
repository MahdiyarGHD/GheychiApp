using Gheychi.Core.Models;
using Gheychi.Core.Services;
using Xunit;

namespace Gheychi.Core.Tests;

public class ContactIndexTests
{
    private static readonly ContactEntry[] Sample =
    [
        new("Sara Mansour", "+1 (555) 671-8200", "Mobile"),
        new("Arash K.", "0912 000 0002", "Mobile"),
        new("arman", "09121234567", "Home"),
        new("علی رضایی", "09351112233", "Mobile"),
        new("1st Pizza", "02188776655", "Work"),
        new("Mara", "09990001111", "Mobile"),
    ];

    private static string[] Names(ContactIndex index, IReadOnlyList<int> matches) =>
        matches.Select(i => index[i].Name).ToArray();

    [Fact]
    public void Entries_AreSortedByLetterWithHashLast()
    {
        var index = new ContactIndex(Sample);

        Assert.Equal(["Arash K.", "arman", "Mara", "Sara Mansour", "علی رضایی", "1st Pizza"],
            Enumerable.Range(0, index.Count).Select(i => index[i].Name));
        Assert.Equal(["A", "A", "M", "S", "ع", "#"],
            Enumerable.Range(0, index.Count).Select(index.LetterAt));
    }

    [Fact]
    public void Match_BlankQuery_ReturnsEverythingInOrder()
    {
        var index = new ContactIndex(Sample);

        Assert.Equal(Enumerable.Range(0, Sample.Length), index.Match("  "));
        Assert.Equal(Enumerable.Range(0, Sample.Length), index.Match(null));
    }

    [Fact]
    public void Match_RanksStartsWithBeforeContains()
    {
        var index = new ContactIndex(Sample);

        // "ara": Arash and Sara both contain it, only Arash starts with it; Mara starts a word? no, contains it.
        var names = Names(index, index.Match("ARA"));

        Assert.Equal(["Arash K.", "Mara", "Sara Mansour"], names);
    }

    [Fact]
    public void Match_FindsByNumberDigitsIgnoringFormatting()
    {
        var index = new ContactIndex(Sample);

        Assert.Equal(["Arash K."], Names(index, index.Match("0912000")));
        Assert.Equal(["Sara Mansour"], Names(index, index.Match("555671")));
    }

    [Fact]
    public void Match_FindsPersianDigitQuery()
    {
        var index = new ContactIndex(Sample);

        Assert.Equal(["علی رضایی"], Names(index, index.Match("۰۹۳۵۱")));
    }

    [Fact]
    public void Match_FindsContactStoredWithPersianDigits()
    {
        var index = new ContactIndex([new ContactEntry("Reza", "۰۹۱۲۳۴۵۶۷۸۹", "Mobile")]);

        Assert.Equal(["Reza"], Names(index, index.Match("0912345")));
    }

    [Fact]
    public void Match_NameMatchesComeBeforeNumberOnlyMatches()
    {
        var index = new ContactIndex(
        [
            new ContactEntry("Zed", "0912111", "Mobile"),
            new ContactEntry("Bob 0912", "5550000", "Mobile"),
        ]);

        Assert.Equal(["Bob 0912", "Zed"], Names(index, index.Match("0912")));
    }

    [Fact]
    public void Match_NoMatch_IsEmpty() =>
        Assert.Empty(new ContactIndex(Sample).Match("zzz"));

    [Fact]
    public void Signature_ChangesWhenAContactChanges()
    {
        var before = new ContactIndex(Sample).Signature;
        var same = new ContactIndex(Sample.Reverse()).Signature;
        var edited = new ContactIndex(Sample.Append(new ContactEntry("New", "0900", "Mobile"))).Signature;

        Assert.Equal(before, same);
        Assert.NotEqual(before, edited);
    }
}
