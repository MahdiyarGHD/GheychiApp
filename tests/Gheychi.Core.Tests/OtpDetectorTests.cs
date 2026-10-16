using Gheychi.Core.Services;
using Xunit;

namespace Gheychi.Core.Tests;

public class OtpDetectorTests
{
    private static string? CodeOf(string text) => OtpDetector.Find(text)?.Code;

    [Theory]
    // English
    [InlineData("Your verification code is 482913", "482913")]
    [InlineData("482913 is your Google verification code.", "482913")]
    [InlineData("G-884213 is your Google verification code.", "884213")]
    [InlineData("Your OTP is 7788. Do not share it with anyone.", "7788")]
    [InlineData("Use 123456 as your Microsoft account security code", "123456")]
    [InlineData("Amazon: 482913 is your one-time password", "482913")]
    [InlineData("Please enter 846291 to verify your account", "846291")]
    [InlineData("Code: 123456", "123456")]
    [InlineData("Your Uber code is 4821. Never share this code.", "4821")]
    [InlineData("<#> Your ExampleApp code is 1234 \nFA+9qCX9VSu", "1234")]
    [InlineData("Your login PIN: 5532 (valid for 5 minutes)", "5532")]
    [InlineData("Verification code\n482913\nValid for 10 minutes", "482913")]
    [InlineData("Your 2FA token is 902114", "902114")]
    [InlineData("Authentication code 33 99 21", null)]
    [InlineData("WhatsApp code 123-456", "123456")]
    [InlineData("Your PayPal security code: 482 913. It expires in 10 minutes.", "482913")]
    [InlineData("Your Acme code is X7K2P9. Do not share it.", "X7K2P9")]
    // Persian
    [InlineData("کد تایید شما: ۴۸۲۱۹", "48219")]
    [InlineData("رمز یکبار مصرف شما: ۱۲۳۴۵۶ این رمز را در اختیار کسی قرار ندهید", "123456")]
    [InlineData("رمز دوم پویا: 123456 مبلغ 25000 ریال", "123456")]
    [InlineData("کد فعال سازی برنامه ۵۵۲۱", "5521")]
    [InlineData("کد ورود شما به دیجی‌کالا: 12345", "12345")]
    [InlineData("کد تایید تراکنش 5599", "5599")]
    [InlineData("۱۲۳۴۵۶ کد تایید شماست", "123456")]
    [InlineData("بانک ملی\nرمز پویا: 123456\nمعتبر تا 2 دقیقه", "123456")]
    [InlineData("سلام، کد ورود به اسنپ: ۷۳۲۱۰. لطفا آن را به کسی ندهید", "73210")]
    // Arabic
    [InlineData("رمز التحقق الخاص بك هو 482913", "482913")]
    [InlineData("كود التحقق: ٤٨٢٩١٣", "482913")]
    // The format a browser or an app can read it from
    [InlineData("Your code is 123456\n\n@example.com #123456", "123456")]
    public void Find_Code(string text, string? expected) => Assert.Equal(expected, CodeOf(text));

    [Theory]
    [InlineData("Order 123456 has shipped")]
    [InlineData("Your tracking code is 123456")]
    [InlineData("کد پیگیری: 123456")]
    [InlineData("کد سفارش شما 556677 است")]
    [InlineData("Use promo code SAVE20 for 20% off")]
    [InlineData("Use code 1234 at checkout for 20% off")]
    [InlineData("Your balance is 5000 rial")]
    [InlineData("مبلغ 25000 ریال برداشت شد")]
    [InlineData("Call 09121234567 for the code")]
    [InlineData("Meeting on 1405.07.29 00:15 code red")]
    [InlineData("Card 6037991111111111 code")]
    [InlineData("Your code is 12")]
    [InlineData("Your code is 123456789012")]
    [InlineData("Happy new year 2024, use code NEWYEAR")]
    [InlineData("Invoice no 556677")]
    [InlineData("Your zip code is 90210")]
    [InlineData("Use code WELCOME5 on your first order")]
    [InlineData("Plain message with nothing in it")]
    [InlineData("")]
    public void Find_NotACode(string text) => Assert.Null(OtpDetector.Find(text));

    [Fact]
    public void Find_Span_IsTheCodeAsWritten()
    {
        const string text = "کد تایید شما: ۴۸۲۱۹";
        var code = OtpDetector.Find(text)!.Value;

        Assert.Equal("۴۸۲۱۹", text.Substring(code.Start, code.Length));
        Assert.Equal("48219", code.Code);
    }

    [Fact]
    public void Find_GroupedCode_SpansTheGroups()
    {
        const string text = "WhatsApp code 123-456";
        var code = OtpDetector.Find(text)!.Value;

        Assert.Equal("123-456", text.Substring(code.Start, code.Length));
        Assert.Equal("123456", code.Code);
    }

    [Theory]
    [InlineData("Your verification code is 482913. Do not share it.")]
    [InlineData("کد تایید شما: ۴۸۲۱۹")]
    [InlineData("Your code is 123456\n\n@example.com #123456")]
    public void IsConfident_WhenTheMessageNamesTheCodePlainly(string text) =>
        Assert.True(OtpDetector.Find(text)!.Value.IsConfident);

    [Fact]
    public void IsConfident_NotWhenTwoNumbersCompete()
    {
        var text = "Your code is 4821 or 5532";
        var codes = OtpDetector.FindAll(text);

        Assert.All(codes, code => Assert.False(code.IsConfident && codes.Any(other => other.Code != code.Code && other.Score > code.Score - 3)));
    }

    [Fact]
    public void FindAll_SkipsWhatTheCallerHasTaken()
    {
        const string text = "Your code is 482913";
        var start = text.IndexOf("482913", StringComparison.Ordinal);

        Assert.Empty(OtpDetector.FindAll(text, (s, l) => s < start + 6 && start < s + l));
    }

    [Fact]
    public void FindAll_SameCodeTwice_IsFoundTwice()
    {
        var codes = OtpDetector.FindAll("Code: 123456. Again: your code 123456");

        Assert.Equal(2, codes.Count);
        Assert.All(codes, code => Assert.Equal("123456", code.Code));
    }

    [Fact]
    public void FindAll_OnNoise_NeverThrowsAndStaysInsideTheText()
    {
        string[] pieces =
        [
            "code", "کد", "رمز", "OTP", "تایید", "verification", "is", "your", "۱۲۳۴۵۶", "123456", "4821", "12", "09121234567",
            "1405.07.29", "*100#", ":", ".", "\n", " ", "-", "#", "%", "ریال", "https://a.com/1234", "X7K2P9", "‌", "@a.com #1234"
        ];
        var random = new Random(7);
        for (var round = 0; round < 3000; round++)
        {
            var text = string.Concat(Enumerable.Range(0, random.Next(1, 14)).Select(_ => pieces[random.Next(pieces.Length)] + (random.Next(3) == 0 ? string.Empty : " ")));
            foreach (var code in OtpDetector.FindAll(text))
            {
                Assert.InRange(code.Start, 0, text.Length);
                Assert.InRange(code.End, code.Start, text.Length);
                Assert.NotEmpty(code.Code);
            }

            _ = TextLinker.Find(text);
        }
    }

    [Fact]
    public void Find_VeryLongMessage_IsLeftAlone() =>
        Assert.Null(OtpDetector.Find("Your code is 482913 " + new string('x', 2000)));
}
