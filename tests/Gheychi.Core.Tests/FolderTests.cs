using System.Text.Json;
using Gheychi.Core.Folders;
using Xunit;

namespace Gheychi.Core.Tests;

public class FolderTests
{
    private static ThreadFacts Facts(
        bool unread = false, bool contact = false, bool sender = false, bool code = false,
        bool failed = false, bool draft = false, bool pinned = false) =>
        new(unread, contact, sender, code, failed, draft, pinned);

    // Folders saved by an older version (reflection serializer) must still load, and are saved back the same.
    [Fact]
    public void Folders_saved_by_the_reflection_serializer_load_and_save_unchanged()
    {
        var old = JsonSerializer.Serialize(new
        {
            Folders = new[]
            {
                new Folder("f1", "Work", "work"),
                new Folder("smart-unread", string.Empty, FolderRules.DefaultIcon, SmartFolder.Unread)
            },
            Members = new Dictionary<string, List<long>> { ["f1"] = [3, 7] }
        });

        var book = FolderBook.Load(old);

        Assert.Equal(2, book.Folders.Count);
        Assert.Equal(SmartFolder.Unread, book.Folders[1].Smart);
        Assert.Equal(old, book.Serialize());
    }

    [Fact]
    public void A_new_book_has_no_folders()
    {
        Assert.Empty(new FolderBook().Folders);
    }

    [Fact]
    public void Added_folders_keep_their_order_and_a_clean_name()
    {
        var book = new FolderBook();
        book.Add("  Work  ", "work");
        book.Add("Family\nand friends that is far too long to fit", "nope");

        Assert.Equal("Work", book.Folders[0].Name);
        Assert.Equal("work", book.Folders[0].IconKey);
        Assert.True(book.Folders[1].Name.Length <= FolderRules.MaxNameLength);
        Assert.DoesNotContain('\n', book.Folders[1].Name);
        Assert.Equal(FolderRules.DefaultIcon, book.Folders[1].IconKey);
    }

    [Fact]
    public void A_smart_folder_can_be_added_once()
    {
        var book = new FolderBook();

        Assert.NotNull(book.AddSmart(SmartFolder.Unread));
        Assert.Null(book.AddSmart(SmartFolder.Unread));
        Assert.True(book.Has(SmartFolder.Unread));
        Assert.Single(book.Folders);
    }

    [Fact]
    public void Only_custom_folders_can_be_renamed()
    {
        var book = new FolderBook();
        var custom = book.Add("Work", "work");
        var smart = book.AddSmart(SmartFolder.Codes)!;

        Assert.True(book.Update(custom.Id, "Office", "home"));
        Assert.Equal(("Office", "home"), (book.Find(custom.Id)!.Name, book.Find(custom.Id)!.IconKey));
        Assert.False(book.Update(smart.Id, "Mine", "star"));
        Assert.False(book.Update(custom.Id, "   ", "star"));
    }

    [Fact]
    public void Moving_stops_at_both_ends()
    {
        var book = new FolderBook();
        var a = book.Add("A", "general");
        var b = book.Add("B", "general");
        var c = book.Add("C", "general");

        Assert.False(book.Move(a.Id, -1));
        Assert.False(book.Move(c.Id, 1));
        Assert.True(book.Move(c.Id, -1));
        Assert.Equal([a.Id, c.Id, b.Id], book.Folders.Select(f => f.Id));
    }

    [Fact]
    public void Membership_is_kept_per_folder_and_dropped_with_it()
    {
        var book = new FolderBook();
        var work = book.Add("Work", "work");
        var home = book.Add("Home", "home");

        Assert.True(book.SetMembership([1, 2, 3], work.Id, true));
        Assert.False(book.SetMembership([1], work.Id, true));
        book.SetMembership([2], home.Id, true);

        Assert.Equal([work.Id, home.Id], book.FoldersOf(2));
        Assert.Equal([work.Id], book.FoldersOf(1));
        Assert.True(book.SetMembership([2], work.Id, false));
        Assert.Equal([home.Id], book.FoldersOf(2));

        book.Remove(work.Id);
        Assert.Empty(book.Members(work.Id));
        Assert.Equal([home.Id], book.FoldersOf(2));
    }

    [Fact]
    public void A_smart_folder_cannot_hold_members()
    {
        var book = new FolderBook();
        var smart = book.AddSmart(SmartFolder.Unread)!;

        Assert.False(book.SetMembership([1], smart.Id, true));
    }

    [Fact]
    public void A_saved_book_loads_back_the_same()
    {
        var book = new FolderBook();
        var work = book.Add("Work", "work");
        book.AddSmart(SmartFolder.Contacts);
        book.SetMembership([5, 9], work.Id, true);

        var loaded = FolderBook.Load(book.Serialize());

        Assert.Equal(book.Folders, loaded.Folders);
        Assert.Equal(new long[] { 5, 9 }, loaded.Members(work.Id).Order());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"Folders\":[{\"Id\":\"x\",\"Name\":\"\",\"IconKey\":\"star\",\"Smart\":null}]}")]
    public void Unreadable_or_nameless_data_gives_no_folders(string? json)
    {
        Assert.Empty(FolderBook.Load(json).Folders);
    }

    [Fact]
    public void All_matches_everything_and_a_custom_folder_only_its_members()
    {
        var all = new Folder(Folder.AllId, string.Empty, "general");
        var custom = new Folder("c", "Work", "work");

        Assert.True(FolderRules.Matches(all, Facts(), isMember: false));
        Assert.True(FolderRules.Matches(custom, Facts(), isMember: true));
        Assert.False(FolderRules.Matches(custom, Facts(unread: true, contact: true), isMember: false));
    }

    [Theory]
    [InlineData(SmartFolder.Unread, true, false, false, false, false, false, false, true)]
    [InlineData(SmartFolder.Unread, false, true, false, false, false, false, false, false)]
    [InlineData(SmartFolder.Contacts, false, true, false, false, false, false, false, true)]
    [InlineData(SmartFolder.Contacts, false, false, true, false, false, false, false, false)]
    [InlineData(SmartFolder.Senders, false, false, true, false, false, false, false, true)]
    [InlineData(SmartFolder.Unknown, false, false, false, false, false, false, false, true)]
    [InlineData(SmartFolder.Unknown, false, true, false, false, false, false, false, false)]
    [InlineData(SmartFolder.Unknown, false, false, true, false, false, false, false, false)]
    [InlineData(SmartFolder.Codes, false, false, false, true, false, false, false, true)]
    [InlineData(SmartFolder.Failed, false, false, false, false, true, false, false, true)]
    [InlineData(SmartFolder.Drafts, false, false, false, false, false, true, false, true)]
    [InlineData(SmartFolder.Pinned, false, false, false, false, false, false, true, true)]
    public void Smart_folders_follow_their_rule(
        SmartFolder smart, bool unread, bool contact, bool sender, bool code, bool failed, bool draft, bool pinned, bool expected)
    {
        var folder = new Folder("s", string.Empty, "general", smart);

        Assert.Equal(expected, FolderRules.Matches(folder, Facts(unread, contact, sender, code, failed, draft, pinned), isMember: false));
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(7, "7")]
    [InlineData(99, "99")]
    [InlineData(100, "99+")]
    [InlineData(5000, "99+")]
    public void The_count_is_capped_at_ninety_nine(int unread, string expected)
    {
        Assert.Equal(expected, FolderRules.CountText(unread));
    }
}
