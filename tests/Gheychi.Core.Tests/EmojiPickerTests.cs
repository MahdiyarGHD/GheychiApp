using Gheychi.Core.Emoji;
using Xunit;

namespace Gheychi.Core.Tests;

public sealed class EmojiPickerTests
{
    private const string Grin = "\U0001F600";
    private const string Wave = "\U0001F44B";
    private const string Family = "\U0001F468‍\U0001F469‍\U0001F467";
    private const string Iran = "\U0001F1EE\U0001F1F7";
    private const string ThumbsTone = "\U0001F44D\U0001F3FD";

    [Fact]
    public void Parse_OneCategoryPerLine_SplitsOnSpaces()
    {
        var groups = EmojiCatalog.Parse($"{Grin} {Grin}\r\n{Wave} a b\n\n");

        Assert.Equal(2, groups.Length);
        Assert.Equal([Grin, Grin], groups[0]);
        Assert.Equal([Wave, "a", "b"], groups[1]);
    }

    [Fact]
    public void Parse_KeepsSequencesWhole()
    {
        var groups = EmojiCatalog.Parse($"{Family} {Iran}");

        Assert.Equal([Family, Iran], groups[0]);
    }

    [Fact]
    public void Push_PutsTheNewestFirst_AndMovesARepeatToTheFront()
    {
        var list = EmojiRecents.Push(["a", "b"], "c");
        Assert.Equal(["c", "a", "b"], list);

        list = EmojiRecents.Push(list, "b");
        Assert.Equal(["b", "c", "a"], list);
    }

    [Fact]
    public void Push_DropsTheOldestPastTheLimit()
    {
        var list = Array.Empty<string>();
        for (var i = 0; i < EmojiRecents.MaxCount + 5; i++)
            list = EmojiRecents.Push(list, "e" + i);

        Assert.Equal(EmojiRecents.MaxCount, list.Length);
        Assert.Equal("e" + (EmojiRecents.MaxCount + 4), list[0]);
        Assert.DoesNotContain("e0", list);
    }

    [Fact]
    public void Recents_RoundTripAndTolerateJunk()
    {
        Assert.Equal([Grin, Wave], EmojiRecents.Parse(EmojiRecents.Serialize([Grin, Wave])));
        Assert.Equal([Grin, Wave], EmojiRecents.Parse($"  {Grin}   {Grin} {Wave} "));
        Assert.Empty(EmojiRecents.Parse(null));
        Assert.Empty(EmojiRecents.Parse(""));
        Assert.Equal(EmojiRecents.MaxCount, EmojiRecents.Parse(string.Join(' ', Enumerable.Range(0, 80).Select(i => "e" + i))).Length);
    }

    [Fact]
    public void Insert_AtTheCaret_MovesTheCaretPastIt()
    {
        var (text, caret) = EmojiTextEdit.Insert("ab", 1, 1, Grin);

        Assert.Equal("a" + Grin + "b", text);
        Assert.Equal(3, caret);
    }

    [Fact]
    public void Insert_ReplacesTheSelection_EvenWhenGivenBackwards()
    {
        var (text, caret) = EmojiTextEdit.Insert("hello", 4, 1, Grin);

        Assert.Equal("h" + Grin + "o", text);
        Assert.Equal(3, caret);
    }

    [Fact]
    public void Insert_IntoNothing_Works()
    {
        Assert.Equal((Grin, 2), EmojiTextEdit.Insert(null, 0, 0, Grin));
    }

    [Theory]
    [InlineData("a" + Grin, 3, "a", 1)]
    [InlineData("a" + Family, 9, "a", 1)]
    [InlineData("a" + Iran + "b", 5, "ab", 1)]
    [InlineData("a" + ThumbsTone, 5, "a", 1)]
    [InlineData("abc", 3, "ab", 2)]
    public void Backspace_RemovesAWholeLetter(string text, int caret, string expected, int expectedCaret)
    {
        Assert.Equal((expected, expectedCaret), EmojiTextEdit.Backspace(text, caret, caret));
    }

    [Fact]
    public void Backspace_DeletesTheSelection_AndDoesNothingAtTheStart()
    {
        Assert.Equal(("ad", 1), EmojiTextEdit.Backspace("abcd", 1, 3));
        Assert.Equal(("abc", 0), EmojiTextEdit.Backspace("abc", 0, 0));
        Assert.Equal(("", 0), EmojiTextEdit.Backspace("", 0, 0));
    }
}
