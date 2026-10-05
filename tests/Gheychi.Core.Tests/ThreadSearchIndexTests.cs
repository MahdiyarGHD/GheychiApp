using Gheychi.Core.Models;
using Gheychi.Core.Services;
using Xunit;

namespace Gheychi.Core.Tests;

public class ThreadSearchIndexTests
{
    // Newest first, as the provider lists them.
    private static readonly ThreadTextRow[] Rows =
    [
        new(5, 5000, "see you tomorrow"),
        new(4, 4000, "Liked “Test message”"),
        new(3, 3000, "Another TEST here"),
        new(2, 2000, "hello"),
        new(1, 1000, "first test")
    ];

    [Fact]
    public void Finds_matches_newest_first_with_their_offset_from_the_newest_message()
    {
        var hits = new ThreadSearchIndex(Rows).Search("test");

        Assert.Equal([new ThreadSearchHit(2, 3), new ThreadSearchHit(4, 1)], hits);
    }

    [Fact]
    public void Reaction_messages_are_not_hits()
    {
        var hits = new ThreadSearchIndex(Rows).Search("Test message");

        Assert.Empty(hits);
    }

    [Fact]
    public void Blank_text_has_no_hits()
    {
        var index = new ThreadSearchIndex(Rows);

        Assert.Empty(index.Search(null));
        Assert.Empty(index.Search("   "));
    }

    [Fact]
    public void Typing_more_narrows_the_previous_hits()
    {
        var index = new ThreadSearchIndex(Rows);
        index.Search("te");

        var hits = index.Search("test");

        Assert.Equal([2, 4], hits.Select(h => h.Row));
    }

    [Fact]
    public void Deleting_text_widens_again()
    {
        var index = new ThreadSearchIndex(Rows);
        index.Search("first test");

        var hits = index.Search("first");

        Assert.Equal([4], hits.Select(h => h.Row));
        Assert.Equal([2, 4], index.Search("test").Select(h => h.Row));
    }

    [Fact]
    public void Row_of_a_message_is_its_offset_from_the_newest()
    {
        var index = new ThreadSearchIndex(Rows);

        Assert.Equal(0, index.RowOf(5));
        Assert.Equal(4, index.RowOf(1));
        Assert.Equal(-1, index.RowOf(99));
    }

    [Fact]
    public void Arabic_and_persian_spellings_match_each_other()
    {
        ThreadTextRow[] rows = [new(1, 1, "سلام علی")];

        Assert.Single(new ThreadSearchIndex(rows).Search("علي"));
    }

    [Fact]
    public void Links_are_found_newest_first_and_other_messages_skipped()
    {
        ThreadTextRow[] rows =
        [
            new(3, 3000, "plain text"),
            new(2, 2000, "go to https://example.com/a and www.example.org"),
            new(1, 1000, "see https://example.com/old")
        ];

        var links = ThreadLinks.Find(rows);

        Assert.Equal([2, 2, 1], links.Select(l => l.MessageId));
        Assert.Equal("example.com", links[0].Item.Host);
    }
}
