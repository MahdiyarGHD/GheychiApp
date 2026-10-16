using Gheychi.Core.Services;
using Xunit;

namespace Gheychi.Core.Tests;

public sealed class SmsStatusHelperTests
{
    [Fact]
    public void HasFailed_FailedType_WinsOverCompleteStatus()
    {
        Assert.True(SmsStatusHelper.HasFailed(SmsStatusHelper.TypeFailed, SmsStatusHelper.StatusComplete));
        Assert.False(SmsStatusHelper.IsDelivered(SmsStatusHelper.TypeFailed, SmsStatusHelper.StatusComplete));
    }

    [Fact]
    public void IsDelivered_SentWithCompleteStatus_IsTrue()
    {
        Assert.True(SmsStatusHelper.IsDelivered(SmsStatusHelper.TypeSent, SmsStatusHelper.StatusComplete));
    }

    [Fact]
    public void IsDelivered_SentWithNoneStatus_IsTrue()
    {
        Assert.True(SmsStatusHelper.IsDelivered(SmsStatusHelper.TypeSent, SmsStatusHelper.StatusNone));
    }

    [Fact]
    public void IsDelivered_InboxWithNoneStatus_IsFalse()
    {
        Assert.False(SmsStatusHelper.IsDelivered(SmsStatusHelper.TypeInbox, SmsStatusHelper.StatusNone));
    }

    [Theory]
    [InlineData(0, DeliveryOutcome.Delivered)]
    [InlineData(1, DeliveryOutcome.Delivered)]
    [InlineData(31, DeliveryOutcome.Delivered)]
    [InlineData(32, DeliveryOutcome.Pending)]
    [InlineData(63, DeliveryOutcome.Pending)]
    [InlineData(64, DeliveryOutcome.Failed)]
    [InlineData(96, DeliveryOutcome.Failed)]
    [InlineData(127, DeliveryOutcome.Failed)]
    [InlineData(-1, DeliveryOutcome.Pending)]
    public void ClassifyReport_ReadsTpStatus(int status, DeliveryOutcome expected) =>
        Assert.Equal(expected, SmsStatusHelper.ClassifyReport(status));

    [Fact]
    public void GetInfoStatus_Incoming_AlwaysReceived()
    {
        Assert.Equal(MessageInfoStatus.Received, SmsStatusHelper.GetInfoStatus(false, false, false, false));
        Assert.Equal(MessageInfoStatus.Received, SmsStatusHelper.GetInfoStatus(false, false, true, false));
        Assert.Equal(MessageInfoStatus.Received, SmsStatusHelper.GetInfoStatus(false, true, true, false));
    }

    [Fact]
    public void GetInfoStatus_OutgoingFailed_IsFailed()
    {
        Assert.Equal(MessageInfoStatus.Failed, SmsStatusHelper.GetInfoStatus(true, true, true, false));
    }

    [Fact]
    public void GetInfoStatus_OutgoingDelivered_IsDelivered()
    {
        Assert.Equal(MessageInfoStatus.Delivered, SmsStatusHelper.GetInfoStatus(true, false, true, false));
    }

    [Fact]
    public void GetInfoStatus_OutgoingSending_IsSending()
    {
        Assert.Equal(MessageInfoStatus.Sending, SmsStatusHelper.GetInfoStatus(true, false, false, true));
    }

    [Fact]
    public void GetInfoStatus_OutgoingNeitherDeliveredNorSending_IsSent()
    {
        Assert.Equal(MessageInfoStatus.Sent, SmsStatusHelper.GetInfoStatus(true, false, false, false));
    }

    [Fact]
    public void SentAwaitingDeliveryReport_IsSentButNotDelivered()
    {
        Assert.True(SmsStatusHelper.IsSent(SmsStatusHelper.TypeSent, SmsStatusHelper.StatusPending));
        Assert.False(SmsStatusHelper.IsDelivered(SmsStatusHelper.TypeSent, SmsStatusHelper.StatusPending));
    }

    [Theory]
    [InlineData(SmsStatusHelper.TypeOutbox, SmsStatusHelper.StatusPending)]
    [InlineData(SmsStatusHelper.TypeFailed, SmsStatusHelper.StatusFailed)]
    [InlineData(SmsStatusHelper.TypeSent, SmsStatusHelper.StatusFailed)]
    [InlineData(SmsStatusHelper.TypeInbox, SmsStatusHelper.StatusNone)]
    public void NotAcceptedByCarrier_IsNotSent(int type, int status)
    {
        Assert.False(SmsStatusHelper.IsSent(type, status));
    }

    [Fact]
    public void Outbox_WithoutAnswer_BecomesUnconfirmedNotFailed()
    {
        const long sentAt = 1_000_000;
        var later = sentAt + SmsStatusHelper.UnconfirmedAfterMs + 1;

        Assert.False(SmsStatusHelper.IsUnconfirmed(SmsStatusHelper.TypeOutbox, sentAt, sentAt + 1000));
        Assert.True(SmsStatusHelper.IsUnconfirmed(SmsStatusHelper.TypeOutbox, sentAt, later));
        Assert.False(SmsStatusHelper.HasFailed(SmsStatusHelper.TypeOutbox, SmsStatusHelper.StatusPending));
    }

    [Theory]
    [InlineData(SmsStatusHelper.TypeSent)]
    [InlineData(SmsStatusHelper.TypeFailed)]
    public void SettledMessage_IsNeverUnconfirmed(int type)
    {
        Assert.False(SmsStatusHelper.IsUnconfirmed(type, 0, SmsStatusHelper.UnconfirmedAfterMs * 10));
    }

    [Fact]
    public void GetInfoStatus_Unconfirmed_IsNeitherSentNorFailed()
    {
        Assert.Equal(MessageInfoStatus.Unconfirmed, SmsStatusHelper.GetInfoStatus(true, false, false, false, isUnconfirmed: true));
        Assert.Equal(MessageInfoStatus.Failed, SmsStatusHelper.GetInfoStatus(true, true, false, false, isUnconfirmed: true));
    }
}
