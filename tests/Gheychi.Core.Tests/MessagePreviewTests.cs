using Gheychi.Core.Services;
using Xunit;

namespace Gheychi.Core.Tests;

public sealed class MessagePreviewTests
{
    [Theory]
    [InlineData("Hello\nworld", "Hello world")]
    [InlineData("Line one \r\n\r\n  line two\nthree", "Line one line two three")]
    [InlineData("کد تایید:\n۱۲۳۴۵\nلغو ۱۱", "کد تایید: ۱۲۳۴۵ لغو ۱۱")]
    [InlineData("Your code\nکد شما: 4821", "Your code کد شما: 4821")]
    [InlineData("\n\nOnly after breaks\n", "Only after breaks")]
    public void LineBreaks_BecomeSingleSpaces(string text, string expected) =>
        Assert.Equal(expected, MessagePreview.SingleLine(text));

    [Fact]
    public void SpacingInsideALine_IsKept()
    {
        Assert.Equal("a  b\tc", MessagePreview.SingleLine("a  b\tc"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \n ")]
    public void Empty_IsEmpty(string? text) => Assert.Equal(string.Empty, MessagePreview.SingleLine(text));

    [Fact]
    public void SingleLine_IsReturnedAsIs()
    {
        const string text = "Short message";
        Assert.Same(text, MessagePreview.SingleLine(text));
    }

    [Fact]
    public void LongMessage_IsCutWithEllipsis()
    {
        var text = string.Join("\n", Enumerable.Repeat("سلام hello", 100));
        var preview = MessagePreview.SingleLine(text);

        Assert.Equal(MessagePreview.MaxLength + 1, preview.Length);
        Assert.EndsWith("…", preview);
        Assert.DoesNotContain('\n', preview);
    }

    [Fact]
    public void Cut_NeverSplitsASurrogatePair()
    {
        var text = new string('a', MessagePreview.MaxLength - 1) + "😀 tail";
        var preview = MessagePreview.SingleLine(text);

        Assert.False(char.IsHighSurrogate(preview[^2]));
        Assert.EndsWith("…", preview);
    }
}
