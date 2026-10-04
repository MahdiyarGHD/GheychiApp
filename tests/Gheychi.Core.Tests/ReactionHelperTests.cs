using Gheychi.Core.Services;
using Xunit;

namespace Gheychi.Core.Tests;

public sealed class ReactionHelperTests
{
    [Fact]
    public void FormatReactionSms_KnownEmoji_UsesTapbackVerbWithCurlyQuotes()
    {
        var result = ReactionHelper.FormatReactionSms("👍", "Hello world, see you soon!");
        Assert.Equal("Liked “Hello world, see you soon!”", result);
    }

    [Fact]
    public void FormatReactionSms_Heart_UsesLovedVerb()
    {
        var result = ReactionHelper.FormatReactionSms("❤️", "Happy birthday!");
        Assert.Equal("Loved “Happy birthday!”", result);
    }

    [Fact]
    public void FormatReactionSms_CustomEmoji_FallsBackToReactedWithCurlyQuotes()
    {
        // No Tapback verb exists for ✂️/🙏/etc. This fallback is NOT our
        // invention: iPhones themselves send exactly this for non-verb emoji
        // (observed on-device: Reacted 🙏 to “حله ممنونم”). Curly quotes required.
        var result = ReactionHelper.FormatReactionSms("✂️", "سلام وقت بخیر");
        Assert.Equal("Reacted ✂️ to “سلام وقت بخیر”", result);
    }

    [Fact]
    public void FormatReactionSms_PersianSnippet_UsesEnglishTemplate()
    {
        // Neither iPhone nor Google Messages recognizes the Persian template,
        // so even Persian snippets use the English wire format.
        var result = ReactionHelper.FormatReactionSms("👍", "سلام خوبی؟");
        Assert.Equal("Liked “سلام خوبی؟”", result);
    }

    [Fact]
    public void FormatReactionSms_LongMessage_TruncatesSnippet()
    {
        var longBody = "This is a very long message that definitely exceeds the snippet length limit of one hundred characters in total length so it will be truncated nicely";
        var result = ReactionHelper.FormatReactionSms("❤️", longBody);
        Assert.StartsWith("Loved “", result);
        Assert.True(result.Length <= 120);
    }

    [Fact]
    public void FormatReactionSms_MultilineBody_PreservesNewlines()
    {
        // iPhone/Google match the snippet verbatim; collapsing \n breaks the match.
        var result = ReactionHelper.FormatReactionSms("👍", "سلام صبح بخیر.\nچشم حتما");
        Assert.Equal("Liked “سلام صبح بخیر.\nچشم حتما”", result);
    }

    [Theory]
    [InlineData("👍", "Liked")]
    [InlineData("❤️", "Loved")]
    [InlineData("😂", "Laughed at")]
    [InlineData("👎", "Disliked")]
    [InlineData("‼️", "Emphasized")]
    [InlineData("❓", "Questioned")]
    [InlineData("✂️", null)]
    [InlineData("🙏", null)]
    public void MapEmojiToVerb_MapsCorrectly(string emoji, string? expectedVerb)
    {
        Assert.Equal(expectedVerb, ReactionHelper.MapEmojiToVerb(emoji));
    }

    [Theory]
    [InlineData("Reacted 👍 to \"See you tomorrow\"", "👍", "See you tomorrow")]
    [InlineData("Reacted ✂️ to \"طرح جدید\"", "✂️", "طرح جدید")]
    [InlineData("Reacted 🙏 to “حله ممنونم”", "🙏", "حله ممنونم")]
    [InlineData("Reacted 🙏 to “سلام صبح بخیر.\nچشم حتما”", "🙏", "سلام صبح بخیر.\nچشم حتما")]
    [InlineData("واکنش ❤️ به «سلام خوبی»", "❤️", "سلام خوبی")]
    [InlineData("واکنش 😮 به \"عکس جدید\"", "😮", "عکس جدید")]
    [InlineData("Liked “Great news”", "👍", "Great news")]
    [InlineData("Loved “Happy birthday”", "❤️", "Happy birthday")]
    [InlineData("Liked “درمورد سیستم‌تون\nفکر کنم چون برق رفته”", "👍", "درمورد سیستم‌تون\nفکر کنم چون برق رفته")]
    public void TryParseReaction_ValidInputs_ParsesSuccessfully(string text, string expectedEmoji, string expectedSnippet)
    {
        var parsed = ReactionHelper.TryParseReaction(text);
        Assert.True(parsed.IsReaction);
        Assert.Equal(expectedEmoji, parsed.Emoji);
        Assert.Equal(expectedSnippet, parsed.Snippet);
    }

    [Theory]
    [InlineData("Just a regular message")]
    [InlineData("Reacted without quotes")]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParseReaction_InvalidInputs_ReturnsFalse(string text)
    {
        var parsed = ReactionHelper.TryParseReaction(text);
        Assert.False(parsed.IsReaction);
    }

    [Fact]
    public void NormalizeForMatch_UnifiesQuotesAndWhitespace()
    {
        Assert.Equal(
            ReactionHelper.NormalizeForMatch("سلام صبح بخیر.\nچشم حتما"),
            ReactionHelper.NormalizeForMatch("سلام صبح بخیر. چشم حتما"));
        Assert.Equal(
            ReactionHelper.NormalizeForMatch("“hello”"),
            ReactionHelper.NormalizeForMatch("\"hello\""));
    }
}
