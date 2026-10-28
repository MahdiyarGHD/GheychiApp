using Gheychi.Core.Services;
using Xunit;

namespace Gheychi.Core.Tests;

public sealed class SmsFailureReasonTests
{
    [Theory]
    [InlineData(1, "RESULT_ERROR_GENERIC_FAILURE (1)")]
    [InlineData(4, "RESULT_ERROR_NO_SERVICE (4)")]
    [InlineData(8, "RESULT_ERROR_SHORT_CODE_NEVER_ALLOWED (8)")]
    [InlineData(10, "RESULT_NETWORK_REJECT (10)")]
    [InlineData(101, "RESULT_RIL_SMS_SEND_FAIL_RETRY (101)")]
    [InlineData(137, "RESULT_RIL_ABORTED (137)")]
    [InlineData(9999, "9999")]
    public void ResultName_NamesKnownCodesAndKeepsTheNumber(int code, string expected) =>
        Assert.Equal(expected, SmsFailureReason.ResultName(code));

    [Fact]
    public void SendFailure_OfOneOfSeveralParts_SaysWhichPartAndTheRadioError() =>
        Assert.Equal("Part 2/2: RESULT_ERROR_GENERIC_FAILURE (1), radio error 38",
            SmsFailureReason.DescribeSendFailure(1, 2, 2, 38));

    [Fact]
    public void SendFailure_OfASinglePart_HasNoPartPrefixAndSkipsAMissingRadioError() =>
        Assert.Equal("RESULT_ERROR_RADIO_OFF (2)", SmsFailureReason.DescribeSendFailure(2, 1, 1, -1));

    [Fact]
    public void ReportFailure_SaysPartStatusAndFormat() =>
        Assert.Equal("Delivery report part 1/2: status 65 (3gpp)", SmsFailureReason.DescribeReportFailure(65, "3gpp", 1, 2));

    [Fact]
    public void ReportFailure_OfASinglePartWithoutFormat() =>
        Assert.Equal("Delivery report: status 64", SmsFailureReason.DescribeReportFailure(64, null, 1, 1));

    [Fact]
    public void Remember_KeepsTheNewestAndReportsWhatFellOut()
    {
        var order = SmsFailureReason.Remember(null, 1, 3, out var none);
        order = SmsFailureReason.Remember(order, 2, 3, out _);
        order = SmsFailureReason.Remember(order, 3, 3, out _);
        Assert.Empty(none);

        order = SmsFailureReason.Remember(order, 4, 3, out var evicted);

        Assert.Equal("2,3,4", order);
        Assert.Equal([1L], evicted);
    }

    [Fact]
    public void Remember_MovesAnIdAlreadyThereToTheEnd()
    {
        var order = SmsFailureReason.Remember("1,2,3", 1, 3, out var evicted);

        Assert.Equal("2,3,1", order);
        Assert.Empty(evicted);
    }
}
