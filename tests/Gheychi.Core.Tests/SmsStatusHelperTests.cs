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
}
