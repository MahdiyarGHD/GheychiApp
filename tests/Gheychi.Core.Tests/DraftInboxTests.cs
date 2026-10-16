using Gheychi.Core.Models;
using Gheychi.Core.Services;
using Xunit;

namespace Gheychi.Core.Tests;

public sealed class DraftInboxTests
{
    private static readonly DateTime Noon = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Local);

    private static SmsThread Thread(long id, DateTime date, string snippet = "last") =>
        new(id, "+98912000000" + id, null, snippet, date, 3, 0, false, 1);

    private static MessageDraft Draft(long id, DateTime date, string text = "typing") =>
        new(id, "+98912000000" + id, "Name", 1, text, new DateTimeOffset(date).ToUnixTimeMilliseconds());

    [Fact]
    public void Merge_NoDrafts_ReturnsTheThreadsAsTheyAre()
    {
        var threads = new[] { Thread(1, Noon), Thread(2, Noon.AddHours(-1)) };

        Assert.Same(threads, DraftInbox.Merge(threads, []));
    }

    [Fact]
    public void Merge_DraftNewerThanTheLastMessage_RaisesTheConversation()
    {
        var threads = new[] { Thread(1, Noon), Thread(2, Noon.AddHours(-2)) };

        var merged = DraftInbox.Merge(threads, [Draft(2, Noon.AddHours(1))]);

        Assert.Equal([2L, 1L], merged.Select(t => t.ThreadId));
        Assert.True(merged[0].HasDraft);
        Assert.Equal("typing", merged[0].Snippet);
        Assert.Equal(Noon.AddHours(1), merged[0].Timestamp);
    }

    [Fact]
    public void Merge_MessageArrivingAfterTheDraft_GoesAboveIt()
    {
        var threads = new[] { Thread(1, Noon.AddHours(2)), Thread(2, Noon.AddHours(-2)) };

        var merged = DraftInbox.Merge(threads, [Draft(2, Noon)]);

        Assert.Equal([1L, 2L], merged.Select(t => t.ThreadId));
        Assert.True(merged[1].HasDraft);
    }

    [Fact]
    public void Merge_DraftOlderThanTheLastMessage_KeepsTheMessageDate()
    {
        var merged = DraftInbox.Merge([Thread(1, Noon)], [Draft(1, Noon.AddHours(-3))]);

        Assert.Equal(Noon, merged[0].Timestamp);
        Assert.True(merged[0].HasDraft);
    }

    [Fact]
    public void Merge_DraftOfAConversationWithoutMessages_IsListed()
    {
        var merged = DraftInbox.Merge([Thread(1, Noon)], [Draft(9, Noon.AddMinutes(5))]);

        Assert.Equal([9L, 1L], merged.Select(t => t.ThreadId));
        Assert.Equal("Name", merged[0].ContactName);
        Assert.True(merged[0].HasDraft);
    }

    [Fact]
    public void Merge_BlankDraft_IsIgnored()
    {
        var threads = new[] { Thread(1, Noon) };

        Assert.Same(threads, DraftInbox.Merge(threads, [Draft(1, Noon.AddHours(1), "  \n ")]));
    }

    [Fact]
    public void Merge_ConversationsOfTheSameMoment_KeepTheirOrder()
    {
        var merged = DraftInbox.Merge([Thread(1, Noon), Thread(2, Noon), Thread(3, Noon)], [Draft(3, Noon)]);

        Assert.Equal([1L, 2L, 3L], merged.Select(t => t.ThreadId));
    }
}
