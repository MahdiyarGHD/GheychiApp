using Gheychi.Core.Models;
using Xunit;

namespace Gheychi.Core.Tests;

public sealed class SearchModelsTests
{
    [Fact]
    public void SearchQuery_DefaultValues_AreCorrect()
    {
        var query = new SearchQuery("contract");
        Assert.Equal("contract", query.Text);
        Assert.Equal(SearchFilterKind.None, query.FilterKind);
        Assert.Null(query.SimSlot);
        Assert.False(query.IncludeArchivedAndSpam);
    }

    [Fact]
    public void SearchQuery_WithFilters_AssignsProperties()
    {
        var query = new SearchQuery(
            Text: "contract",
            FilterKind: SearchFilterKind.Unread,
            SimSlot: 1,
            IncludeArchivedAndSpam: true
        );

        Assert.Equal("contract", query.Text);
        Assert.Equal(SearchFilterKind.Unread, query.FilterKind);
        Assert.Equal(1, query.SimSlot);
        Assert.True(query.IncludeArchivedAndSpam);
    }

    [Fact]
    public void SearchResultChat_RecordsProperties_Properly()
    {
        var now = DateTime.Now;
        var result = new SearchResultChat(
            ThreadId: 42,
            MessageId: 101,
            Address: "+1234567890",
            ContactName: "Elena Rostova",
            Snippet: "Signed contract",
            Timestamp: now,
            SubId: 1,
            TotalMatches: 2,
            IsRead: false,
            IsArchived: false,
            IsSpam: false
        );

        Assert.Equal(42, result.ThreadId);
        Assert.Equal(101, result.MessageId);
        Assert.Equal("Elena Rostova", result.ContactName);
        Assert.Equal(2, result.TotalMatches);
        Assert.False(result.IsRead);
    }
}
