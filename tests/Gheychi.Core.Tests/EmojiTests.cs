using Gheychi.Core.Services;
using Xunit;

namespace Gheychi.Core.Tests;

public class EmojiTests
{
    [Fact]
    public void Every_category_has_emoji_and_no_emoji_is_listed_twice()
    {
        var all = EmojiCatalog.Categories.SelectMany(c => c.Emojis).ToList();

        Assert.All(EmojiCatalog.Categories, c => Assert.NotEmpty(c.Emojis));
        Assert.Equal(all.Count, all.Distinct().Count());
    }

    [Fact]
    public void Emoji_hold_no_whitespace_so_recents_can_be_stored_space_separated()
    {
        var all = EmojiCatalog.Categories.SelectMany(c => c.Emojis);

        Assert.All(all, e => Assert.DoesNotContain(e, char.IsWhiteSpace));
    }

    [Fact]
    public void Recent_is_newest_first_without_repeats_and_capped()
    {
        var recent = new RecentEmojis(3);
        recent.Add("😀");
        recent.Add("😂");
        recent.Add("😀");
        recent.Add("🔥");
        recent.Add("👍");

        Assert.Equal(["👍", "🔥", "😀"], recent.Items);
    }

    [Fact]
    public void Recent_survives_storing_and_loading()
    {
        var recent = new RecentEmojis();
        recent.Add("😀");
        recent.Add("❤️");

        Assert.Equal(["❤️", "😀"], RecentEmojis.Parse(recent.Serialize()).Items);
        Assert.Empty(RecentEmojis.Parse(null).Items);
    }

    [Fact]
    public void Insert_puts_the_emoji_at_the_cursor()
    {
        var (text, cursor) = EmojiText.Insert("hello world", 5, "😀");

        Assert.Equal("hello😀 world", text);
        Assert.Equal(7, cursor);
    }

    [Fact]
    public void Insert_keeps_a_cursor_outside_the_text_inside_it()
    {
        Assert.Equal(("a😀", 3), EmojiText.Insert("a", 99, "😀"));
        Assert.Equal(("😀a", 2), EmojiText.Insert("a", -4, "😀"));
        Assert.Equal(("😀", 2), EmojiText.Insert(null, 0, "😀"));
    }

    [Fact]
    public void Delete_removes_a_whole_emoji_not_half_of_it()
    {
        Assert.Equal(("a", 1), EmojiText.DeleteBefore("a😀", 3));
        Assert.Equal(("a", 1), EmojiText.DeleteBefore("a❤️", 3));
    }

    [Fact]
    public void Delete_removes_a_joined_sequence_as_one_character()
    {
        Assert.Equal(("a", 1), EmojiText.DeleteBefore("a👨‍👩‍👧", "a👨‍👩‍👧".Length));
    }

    [Fact]
    public void Delete_at_the_start_does_nothing()
    {
        Assert.Equal(("abc", 0), EmojiText.DeleteBefore("abc", 0));
        Assert.Equal(("", 0), EmojiText.DeleteBefore(null, 0));
    }
}
