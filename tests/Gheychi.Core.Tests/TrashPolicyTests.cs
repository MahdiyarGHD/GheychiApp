using Gheychi.Core.Models;
using Gheychi.Core.Services;
using Xunit;

namespace Gheychi.Core.Tests;

public sealed class TrashPolicyTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Local);

    private static long Ms(DateTime time) => new DateTimeOffset(time).ToUnixTimeMilliseconds();

    private static TrashedMessage Message(string address, DateTime date, long trashedAt, int type = 1) =>
        new(0, address, "text", Ms(date), type, 1, trashedAt);

    [Fact]
    public void Cutoff_IsRetentionDaysBack() => Assert.Equal(Now.AddDays(-30), TrashPolicy.Cutoff(Now));

    [Theory]
    [InlineData(0, 30)]
    [InlineData(10, 20)]
    [InlineData(29, 1)]
    [InlineData(35, 1)]
    public void DaysLeft_CountsDownAndStopsAtOne(int daysAgo, int expected) =>
        Assert.Equal(expected, TrashPolicy.DaysLeft(Ms(Now.AddDays(-daysAgo)), Now));

    [Theory]
    [InlineData(SmsStatusHelper.TypeInbox, SmsStatusHelper.TypeInbox)]
    [InlineData(SmsStatusHelper.TypeSent, SmsStatusHelper.TypeSent)]
    [InlineData(SmsStatusHelper.TypeFailed, SmsStatusHelper.TypeFailed)]
    [InlineData(SmsStatusHelper.TypeOutbox, SmsStatusHelper.TypeFailed)]
    [InlineData(SmsStatusHelper.TypeQueued, SmsStatusHelper.TypeFailed)]
    [InlineData(SmsStatusHelper.TypeDraft, SmsStatusHelper.TypeFailed)]
    public void RestoredType_OnlyUnsentOnesBecomeFailed(int type, int expected) =>
        Assert.Equal(expected, TrashPolicy.RestoredType(type));

    [Fact]
    public void Group_OneItemPerSenderAndDeletion_NewestDeletionFirst()
    {
        var earlier = Ms(Now.AddDays(-2));
        var later = Ms(Now.AddDays(-1));
        var groups = TrashPolicy.Group(
        [
            Message("A", Now.AddHours(-5), earlier),
            Message("A", Now.AddHours(-4), earlier),
            Message("B", Now.AddHours(-3), later),
            Message("A", Now.AddHours(-2), later)
        ]);

        Assert.Equal(3, groups.Count);
        Assert.Equal([later, later, earlier], groups.Select(g => g.TrashedAtMs));
        var a = groups.Single(g => g.Address == "A" && g.TrashedAtMs == earlier);
        Assert.Equal(2, a.Messages.Count);
        Assert.Equal(Ms(Now.AddHours(-4)), a.Newest.DateMs);
    }

    [Fact]
    public void Group_OneChatWithTheNumberWrittenTwoWays_IsOneItemUnderTheReceivedForm()
    {
        var at = Ms(Now.AddDays(-1));
        var groups = TrashPolicy.Group(
        [
            Message("+989121234567", Now.AddHours(-3), at, SmsStatusHelper.TypeSent),
            Message("09121234567", Now.AddHours(-2), at, SmsStatusHelper.TypeInbox),
            Message("+989121234567", Now.AddHours(-1), at, SmsStatusHelper.TypeSent)
        ]);

        var group = Assert.Single(groups);
        Assert.Equal("09121234567", group.Address);
        Assert.Equal(3, group.Messages.Count);
    }

    [Fact]
    public void ConversationAddresses_PrefersTheReceivedForm_ElseTheFirst()
    {
        var at = Ms(Now);
        var addresses = TrashPolicy.ConversationAddresses(
        [
            Message("+989121234567", Now, at, SmsStatusHelper.TypeSent),
            Message("09121234567", Now, at, SmsStatusHelper.TypeInbox),
            Message("+989351112222", Now, at, SmsStatusHelper.TypeSent)
        ]);

        Assert.Equal(2, addresses.Count);
        Assert.Equal("09121234567", addresses[PhoneNumberNormalizer.ToLookupKey("+989121234567")]);
        Assert.Equal("+989351112222", addresses[PhoneNumberNormalizer.ToLookupKey("09351112222")]);
    }
}
