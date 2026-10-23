using Gheychi.Core.Services;
using Xunit;

namespace Gheychi.Core.Tests;

public class EmojiTextTests
{
    [Theory]
    [InlineData("😀", 1)]
    [InlineData("  😀 \n", 1)]
    [InlineData("😀😂", 2)]
    [InlineData("😀 😂 🥹", 3)]
    [InlineData("👍🏽", 1)]
    [InlineData("👨‍👩‍👧‍👦", 1)]
    [InlineData("🏳️‍🌈", 1)]
    [InlineData("🇮🇷", 1)]
    [InlineData("🇮🇷🇩🇪", 2)]
    [InlineData("❤️", 1)]
    [InlineData("1️⃣", 1)]
    [InlineData("⭐", 1)]
    [InlineData("✅❌", 2)]
    [InlineData("👩🏽‍💻🙏❤️", 3)]
    public void BigCount_OnlyEmoji_CountsWhatTheUserSees(string text, int expected) =>
        Assert.Equal(expected, EmojiText.BigCount(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hi")]
    [InlineData("hi 😀")]
    [InlineData("😀 hi")]
    [InlineData("😀😂🥹😍")]
    [InlineData("😀 1")]
    [InlineData("1")]
    [InlineData("#")]
    [InlineData("🇮")]
    [InlineData("❤")]
    [InlineData("✔")]
    [InlineData("سلام 😀")]
    [InlineData("😀.")]
    public void BigCount_AnythingElse_IsZero(string? text) =>
        Assert.Equal(0, EmojiText.BigCount(text));
}
