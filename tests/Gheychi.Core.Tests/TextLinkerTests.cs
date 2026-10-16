using Gheychi.Core.Services;
using Xunit;

namespace Gheychi.Core.Tests;

public class TextLinkerTests
{
    private static string[] Shown(string text, LinkKind kind) =>
        (TextLinker.Find(text) ?? [])
        .Where(span => span.Kind == kind)
        .Select(span => text.Substring(span.Start, span.Length))
        .ToArray();

    [Theory]
    [InlineData("Plain message with no links.")]
    [InlineData("Your code is 123456")]
    [InlineData("On 2024-01-15 at 10:30")]
    [InlineData("Card 6037 9911 1111 1111 expires soon")]
    [InlineData("file.txt and hello.world")]
    [InlineData("")]
    public void Find_PlainText_ReturnsNull(string text) => Assert.Null(TextLinker.Find(text));

    [Fact]
    public void Find_Url_KeepsTheTextAfterIt()
    {
        var text = "Pay at https://pay.example.com/x?id=1 before Friday";

        Assert.Equal(["https://pay.example.com/x?id=1"], Shown(text, LinkKind.Url));
    }

    [Theory]
    [InlineData("Open (https://a.com/b) now", "https://a.com/b")]
    [InlineData("Open https://a.com/b. Then", "https://a.com/b")]
    [InlineData("Open https://a.com/b، سپس", "https://a.com/b")]
    [InlineData("Wiki https://a.com/x_(y) page", "https://a.com/x_(y)")]
    public void Find_Url_LeavesTrailingPunctuationOut(string text, string expected) =>
        Assert.Equal([expected], Shown(text, LinkKind.Url));

    [Theory]
    [InlineData("go to bit.ly/abc123 now", "bit.ly/abc123")]
    [InlineData("see www.example.ir", "www.example.ir")]
    [InlineData("mail me at a@example.com", null)]
    public void Find_UrlWithoutScheme(string text, string? expected) =>
        Assert.Equal(expected is null ? [] : [expected], Shown(text, LinkKind.Url));

    [Theory]
    [InlineData("Call +98 912 345 6789 now", "+98 912 345 6789")]
    [InlineData("Call 09121234567.", "09121234567")]
    [InlineData("Call (021) 8877 6655 today", "021) 8877 6655")]
    [InlineData("tel: 555-123-4567", "555-123-4567")]
    [InlineData("شماره ۰۹۱۲۳۴۵۶۷۸۹ را بگیرید", "۰۹۱۲۳۴۵۶۷۸۹")]
    public void Find_Phone(string text, string expected) =>
        Assert.Equal([expected], Shown(text, LinkKind.Phone));

    [Fact]
    public void Find_TwoNumbersSideBySide_AreTwo() =>
        Assert.Equal(["09121234567", "09351234567"], Shown("09121234567 09351234567", LinkKind.Phone));

    [Fact]
    public void Find_UrlDigitsAreNotAPhone()
    {
        var text = "https://a.com/1234567890123";

        Assert.Empty(Shown(text, LinkKind.Phone));
        Assert.Single(Shown(text, LinkKind.Url));
    }

    [Fact]
    public void Find_ReturnsSpansInOrder()
    {
        var text = "09121234567 or https://a.com";
        var spans = TextLinker.Find(text)!;

        Assert.Equal([LinkKind.Phone, LinkKind.Url], spans.Select(s => s.Kind));
    }

    [Theory]
    [InlineData("see www.a.com", "https://www.a.com")]
    [InlineData("see http://a.com/x", "http://a.com/x")]
    [InlineData("bit.ly/x", "https://bit.ly/x")]
    public void Target_Url_HasAScheme(string text, string expected)
    {
        var span = TextLinker.Find(text)!.Single();

        Assert.Equal(expected, TextLinker.Target(text, span));
    }

    [Theory]
    [InlineData("Call +98 912 345 6789", "+989123456789")]
    [InlineData("Call 0912-345-6789", "09123456789")]
    [InlineData("شماره ۰۹۱۲۳۴۵۶۷۸۹", "09123456789")]
    public void Target_Phone_IsDigits(string text, string expected)
    {
        var span = TextLinker.Find(text)!.Single();

        Assert.Equal(expected, TextLinker.Target(text, span));
    }

    [Theory]
    [InlineData("Dial *815*3453# now", "*815*3453#")]
    [InlineData("کد *۸۱۵*۳۴۵۳# را بگیرید", "*۸۱۵*۳۴۵۳#")]
    [InlineData("Balance: *100#.", "*100#")]
    [InlineData("IMEI *#06# shows it", "*#06#")]
    [InlineData("Send *123*1*2# or #777#", "*123*1*2#")]
    public void Find_Ussd(string text, string expected) =>
        Assert.Equal([expected], Shown(text, LinkKind.Ussd).Take(1));

    [Theory]
    [InlineData("C# and F# are languages")]
    [InlineData("Order #1234 shipped")]
    [InlineData("Rated 5*** by users")]
    [InlineData("###")]
    [InlineData("Code *123#4 is wrong")]
    public void Find_NotUssd(string text) => Assert.Empty(Shown(text, LinkKind.Ussd));

    [Theory]
    [InlineData("*815*3453#", "*815*3453#")]
    [InlineData("*۸۱۵*۳۴۵۳#", "*815*3453#")]
    public void Target_Ussd_KeepsStarsAndHashesWithLatinDigits(string text, string expected)
    {
        var span = TextLinker.Find(text)!.Single();

        Assert.Equal(LinkKind.Ussd, span.Kind);
        Assert.Equal(expected, TextLinker.Target(text, span));
    }

    [Theory]
    [InlineData("1405.07.29 00")]
    [InlineData("تاریخ 1405.07.29 00:15 مبلغ")]
    [InlineData("on 2024/01/15 10:30:45 paid")]
    [InlineData("پرداخت ۱۴۰۵/۰۷/۲۹ ۲۳:۵۹")]
    [InlineData("15.01.2024 12")]
    [InlineData("1405-07-29 09")]
    public void Find_DateAndTime_AreNotAPhone(string text) => Assert.Null(TextLinker.Find(text));

    [Fact]
    public void Find_PhoneNextToADate_StaysAPhone()
    {
        const string text = "1405.07.29 09121234567";

        Assert.Equal(["09121234567"], Shown(text, LinkKind.Phone));
    }

    [Fact]
    public void Find_PhoneWithDottedGroups_IsNotTakenForADate()
    {
        const string text = "Call 0912.345.6789";

        Assert.Equal(["0912.345.6789"], Shown(text, LinkKind.Phone));
    }

    [Fact]
    public void Find_PhoneUssdAndUrl_KeepEach()
    {
        const string text = "Call 09121234567 or dial *100# or open https://a.com/x#top";
        var spans = TextLinker.Find(text)!;

        Assert.Equal([LinkKind.Phone, LinkKind.Ussd, LinkKind.Url], spans.Select(s => s.Kind));
    }
}
