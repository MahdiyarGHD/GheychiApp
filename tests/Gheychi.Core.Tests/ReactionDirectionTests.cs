using Gheychi.Core.Models;
using Gheychi.Core.Services;
using Xunit;

namespace Gheychi.Core.Tests;

// Regression: on a dual-SIM phone, reacting to a message received on SIM 2 was sent from SIM 1,
// because received messages were stored without sub_id and the sender fell back to slot 1.
public sealed class ReactionDirectionTests
{
    private static readonly IReadOnlyDictionary<int, int> SubToSlot = new Dictionary<int, int> { [11] = 1, [22] = 2 };

    [Fact]
    public void ReactionToMessageReceivedOnSim2_IsSentFromSim2()
    {
        Assert.Equal(22, SimResolver.ResolveSendSubId(messageSubId: 22, selectedSubId: 11, selectedSlot: 1, SubToSlot));
    }

    [Fact]
    public void MessageWithoutSubscription_UsesSimSelectedInChat_NotSlotOne()
    {
        Assert.Equal(22, SimResolver.ResolveSendSubId(messageSubId: -1, selectedSubId: 22, selectedSlot: 2, SubToSlot));
        Assert.Equal(22, SimResolver.ResolveSendSubId(messageSubId: 0, selectedSubId: 0, selectedSlot: 2, SubToSlot));
    }

    [Fact]
    public void NothingKnown_FallsBackToSlotNumber()
    {
        Assert.Equal(2, SimResolver.ResolveSendSubId(-1, 0, 2, null));
        Assert.Equal(0, SimResolver.ResolveSendSubId(-1, 0, 0, null));
    }

    [Fact]
    public void SimToSimReaction_AttachesToOriginalMessage_EvenWhenClocksDisagree()
    {
        // #1 sends "Test" (row stamped after the send callback), #2 reacts immediately; the
        // reaction arrives stamped by the network clock a few seconds before the local row.
        var sentAt = new DateTime(2026, 10, 4, 12, 0, 5);
        var test = new SmsMessage(1, 7, "+989121110000", "Test", sentAt, true, true, false, 11);
        var reaction = new SmsMessage(2, 7, "+989121110000", "Laughed at “Test”", sentAt.AddSeconds(-3), false, true, false, 11);

        var target = ReactionHelper.FindReactionTarget([test, reaction], reaction, "Test");

        Assert.NotNull(target);
        Assert.Equal(1, target!.Id);
    }

    [Fact]
    public void LaterDuplicateBody_IsNotPreferredOverEarlierTarget()
    {
        var t0 = new DateTime(2026, 10, 4, 12, 0, 0);
        var earlier = new SmsMessage(1, 7, "+1", "Test", t0, true, true, false, 11);
        var later = new SmsMessage(2, 7, "+1", "Test", t0.AddSeconds(90), true, true, false, 11);
        var reaction = new SmsMessage(3, 7, "+1", "Laughed at “Test”", t0.AddSeconds(30), false, true, false, 11);

        var target = ReactionHelper.FindReactionTarget([earlier, later, reaction], reaction, "Test");

        Assert.Equal(1, target!.Id);
    }

    [Fact]
    public void MessageFarInTheFuture_IsNotATarget()
    {
        var t0 = new DateTime(2026, 10, 4, 12, 0, 0);
        var future = new SmsMessage(1, 7, "+1", "Test", t0.AddMinutes(30), true, true, false, 11);
        var reaction = new SmsMessage(2, 7, "+1", "Laughed at “Test”", t0, false, true, false, 11);

        Assert.Null(ReactionHelper.FindReactionTarget([future, reaction], reaction, "Test"));
    }

    [Fact]
    public void ReactionOnSameSideMessage_IsNotATarget()
    {
        var t0 = new DateTime(2026, 10, 4, 12, 0, 0);
        var incomingTest = new SmsMessage(1, 7, "+1", "Test", t0, false, true, false, 22);
        var incomingReaction = new SmsMessage(2, 7, "+1", "Laughed at “Test”", t0.AddSeconds(5), false, true, false, 11);

        Assert.Null(ReactionHelper.FindReactionTarget([incomingTest, incomingReaction], incomingReaction, "Test"));
    }
}
