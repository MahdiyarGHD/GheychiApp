using Gheychi.Core.Models;
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

    [Theory]
    [InlineData("👍", "Hello!", "Liked “Hello!”")]
    [InlineData("❤️", "Hello!", "Loved “Hello!”")]
    [InlineData("😂", "lililili", "Laughed at “lililili”")]
    [InlineData("👎", "Hello!", "Disliked “Hello!”")]
    [InlineData("‼️", "Hello!", "Emphasized “Hello!”")]
    [InlineData("❓", "Hello!", "Questioned “Hello!”")]
    public void FormatReactionSms_AllSixDockEmoji_UseVerbTemplate(string emoji, string body, string expected)
    {
        // Exactly the 6 emoji the dock offers — all must produce the verb template.
        Assert.Equal(expected, ReactionHelper.FormatReactionSms(emoji, body));
    }

    [Theory]
    [InlineData("✂️")]
    [InlineData("🙏")]
    [InlineData("😮")]
    [InlineData("😢")]
    public void FormatReactionSms_UnmappedEmoji_Throws(string emoji)
    {
        // No Tapback verb exists for these, so no template renders as a reaction
        // on other SMS apps. The dock no longer offers them; fail fast instead.
        Assert.Throws<ArgumentException>(() => ReactionHelper.FormatReactionSms(emoji, "hello"));
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
    public void FormatReactionSms_LongMultilineMessage_QuotesWholeMessage()
    {
        var longBody = "This is a very long message that definitely exceeds one hundred characters in total length\nand it continues on a second line\r\nand a third one.";
        var result = ReactionHelper.FormatReactionSms("❤️", longBody);
        Assert.Equal("Loved “" + longBody.Replace("\r\n", "\n") + "”", result);
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

    private static SmsMessage Msg(long id, string body, DateTime ts, bool outgoing) =>
        new(id, 1, "+1", body, ts, outgoing, true, false, 1);

    [Fact]
    public void FindReactionTarget_DuplicateBodies_PicksNearestBeforeReaction()
    {
        var t0 = new DateTime(2026, 10, 2, 12, 0, 0);
        var first = Msg(1, "Hahahaha", t0, outgoing: false);
        var second = Msg(2, "Hahahaha", t0.AddMinutes(1), outgoing: false);
        var third = Msg(3, "Hahahaha", t0.AddMinutes(3), outgoing: false);
        var reaction = Msg(4, "Laughed at “Hahahaha”", t0.AddMinutes(2), outgoing: true);
        var messages = new List<SmsMessage> { first, second, third, reaction };

        Assert.Same(second, ReactionHelper.FindReactionTarget(messages, reaction, "Hahahaha"));
    }

    [Fact]
    public void FindReactionTarget_IgnoresMessagesAfterReaction()
    {
        var t0 = new DateTime(2026, 10, 2, 12, 0, 0);
        var before = Msg(1, "Hahahaha", t0, outgoing: false);
        var after = Msg(2, "Hahahaha", t0.AddMinutes(5), outgoing: false);
        var reaction = Msg(3, "Laughed at “Hahahaha”", t0.AddMinutes(1), outgoing: true);

        Assert.Same(before, ReactionHelper.FindReactionTarget([before, after, reaction], reaction, "Hahahaha"));
    }

    [Fact]
    public void FindReactionTarget_IgnoresSameSideBubbles()
    {
        var t0 = new DateTime(2026, 10, 2, 12, 0, 0);
        var mine = Msg(1, "Hahahaha", t0, outgoing: true);
        var theirs = Msg(2, "Hahahaha", t0.AddMinutes(1), outgoing: false);
        var reaction = Msg(3, "Laughed at “Hahahaha”", t0.AddMinutes(2), outgoing: true);

        Assert.Same(theirs, ReactionHelper.FindReactionTarget([mine, theirs, reaction], reaction, "Hahahaha"));
    }

    [Fact]
    public void FindReactionTarget_RejectsMidStringSubstring()
    {
        var t0 = new DateTime(2026, 10, 2, 12, 0, 0);
        var candidate = Msg(1, "Say Hahahaha now", t0, outgoing: false);
        var reaction = Msg(2, "Laughed at “Hahahaha”", t0.AddMinutes(1), outgoing: true);

        Assert.Null(ReactionHelper.FindReactionTarget([candidate, reaction], reaction, "Hahahaha"));
    }

    [Fact]
    public void FindReactionTarget_AcceptsTruncatedPrefix()
    {
        var longBody = new string('a', 150);
        var t0 = new DateTime(2026, 10, 2, 12, 0, 0);
        var candidate = Msg(1, longBody, t0, outgoing: false);
        var snippet = ReactionHelper.GetSnippet(longBody);
        var reaction = Msg(2, $"Liked “{snippet}”", t0.AddMinutes(1), outgoing: true);

        Assert.Same(candidate, ReactionHelper.FindReactionTarget([candidate, reaction], reaction, snippet));
    }

    [Fact]
    public void FindReactionTarget_SkipsOtherReactionSms()
    {
        var t0 = new DateTime(2026, 10, 2, 12, 0, 0);
        var target = Msg(1, "Hahahaha", t0, outgoing: false);
        var otherReaction = Msg(2, "Liked “Hahahaha”", t0.AddSeconds(30), outgoing: true);
        var reaction = Msg(3, "Laughed at “Hahahaha”", t0.AddMinutes(1), outgoing: true);

        Assert.Same(target, ReactionHelper.FindReactionTarget([target, otherReaction, reaction], reaction, "Hahahaha"));
    }
}
