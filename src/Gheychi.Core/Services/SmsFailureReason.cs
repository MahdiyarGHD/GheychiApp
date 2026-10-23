namespace Gheychi.Core.Services;

/// <summary>
/// Words for why an outgoing message failed, from what the platform reported for each part: the result code of the
/// sent broadcast (SmsManager.RESULT_*), the radio's own cause ("errorCode" extra), and the TP-Status of a delivery report.
/// The text is for support and logs, so it stays in English and always carries the raw numbers.
/// </summary>
public static class SmsFailureReason
{
    private static readonly Dictionary<int, string> ResultNames = new()
    {
        [0] = "RESULT_ERROR_NONE",
        [1] = "RESULT_ERROR_GENERIC_FAILURE",
        [2] = "RESULT_ERROR_RADIO_OFF",
        [3] = "RESULT_ERROR_NULL_PDU",
        [4] = "RESULT_ERROR_NO_SERVICE",
        [5] = "RESULT_ERROR_LIMIT_EXCEEDED",
        [6] = "RESULT_ERROR_FDN_CHECK_FAILURE",
        [7] = "RESULT_ERROR_SHORT_CODE_NOT_ALLOWED",
        [8] = "RESULT_ERROR_SHORT_CODE_NEVER_ALLOWED",
        [9] = "RESULT_RADIO_NOT_AVAILABLE",
        [10] = "RESULT_NETWORK_REJECT",
        [11] = "RESULT_INVALID_ARGUMENTS",
        [12] = "RESULT_INVALID_STATE",
        [13] = "RESULT_NO_MEMORY",
        [14] = "RESULT_INVALID_SMS_FORMAT",
        [15] = "RESULT_SYSTEM_ERROR",
        [16] = "RESULT_MODEM_ERROR",
        [17] = "RESULT_NETWORK_ERROR",
        [18] = "RESULT_ENCODING_ERROR",
        [19] = "RESULT_INVALID_SMSC_ADDRESS",
        [20] = "RESULT_OPERATION_NOT_ALLOWED",
        [21] = "RESULT_INTERNAL_ERROR",
        [22] = "RESULT_NO_RESOURCES",
        [23] = "RESULT_CANCELLED",
        [24] = "RESULT_REQUEST_NOT_SUPPORTED",
        [25] = "RESULT_NO_BLUETOOTH_SERVICE",
        [26] = "RESULT_INVALID_BLUETOOTH_ADDRESS",
        [27] = "RESULT_BLUETOOTH_DISCONNECTED",
        [28] = "RESULT_UNEXPECTED_EVENT_STOP_SENDING",
        [29] = "RESULT_SMS_BLOCKED_DURING_EMERGENCY",
        [30] = "RESULT_SMS_SEND_RETRY_FAILED",
        [31] = "RESULT_REMOTE_EXCEPTION",
        [32] = "RESULT_NO_DEFAULT_SMS_APP",
        [33] = "RESULT_USER_NOT_ALLOWED",
        [100] = "RESULT_RIL_RADIO_NOT_AVAILABLE",
        [101] = "RESULT_RIL_SMS_SEND_FAIL_RETRY",
        [102] = "RESULT_RIL_NETWORK_REJECT",
        [103] = "RESULT_RIL_INVALID_STATE",
        [104] = "RESULT_RIL_INVALID_ARGUMENTS",
        [105] = "RESULT_RIL_NO_MEMORY",
        [106] = "RESULT_RIL_REQUEST_RATE_LIMITED",
        [107] = "RESULT_RIL_INVALID_SMS_FORMAT",
        [108] = "RESULT_RIL_SYSTEM_ERR",
        [109] = "RESULT_RIL_ENCODING_ERR",
        [110] = "RESULT_RIL_INVALID_SMSC_ADDRESS",
        [111] = "RESULT_RIL_MODEM_ERR",
        [112] = "RESULT_RIL_NETWORK_ERR",
        [113] = "RESULT_RIL_INTERNAL_ERR",
        [114] = "RESULT_RIL_REQUEST_NOT_SUPPORTED",
        [115] = "RESULT_RIL_INVALID_MODEM_STATE",
        [116] = "RESULT_RIL_NETWORK_NOT_READY",
        [117] = "RESULT_RIL_OPERATION_NOT_ALLOWED",
        [118] = "RESULT_RIL_NO_RESOURCES",
        [119] = "RESULT_RIL_CANCELLED",
        [120] = "RESULT_RIL_SIM_ABSENT",
        [121] = "RESULT_RIL_SIMULTANEOUS_SMS_AND_CALL_NOT_ALLOWED",
        [122] = "RESULT_RIL_ACCESS_BARRED",
        [123] = "RESULT_RIL_BLOCKED_DUE_TO_CALL",
        [124] = "RESULT_RIL_GENERIC_ERROR",
        [125] = "RESULT_RIL_INVALID_RESPONSE",
        [128] = "RESULT_RIL_SUBSCRIPTION_NOT_AVAILABLE",
        [129] = "RESULT_RIL_SIM_ERROR",
        [130] = "RESULT_RIL_INVALID_SIM_STATE",
        [131] = "RESULT_RIL_NO_SMS_TO_ACK",
        [132] = "RESULT_RIL_SIM_BUSY",
        [133] = "RESULT_RIL_SIM_FULL",
        [134] = "RESULT_RIL_NO_SUBSCRIPTION",
        [135] = "RESULT_RIL_NO_NETWORK_FOUND",
        [136] = "RESULT_RIL_DEVICE_IN_USE",
        [137] = "RESULT_RIL_ABORTED",
    };

    /// <summary>"RESULT_ERROR_GENERIC_FAILURE (1)"; just the number for a code this table does not know.</summary>
    public static string ResultName(int resultCode) =>
        ResultNames.TryGetValue(resultCode, out var name) ? $"{name} ({resultCode})" : resultCode.ToString();

    /// <summary>
    /// What went wrong with one part of the send, e.g. "Part 2/2: RESULT_ERROR_GENERIC_FAILURE (1), radio error 38".
    /// <paramref name="part"/> counts from 1; the part prefix is left out for a single-part message.
    /// <paramref name="radioError"/> is the "errorCode" extra, or -1 when the platform gave none.
    /// </summary>
    public static string DescribeSendFailure(int resultCode, int part, int parts, int radioError)
    {
        var text = ResultName(resultCode);
        if (radioError > 0)
            text += $", radio error {radioError}";
        return Prefix(part, parts, string.Empty) + text;
    }

    /// <summary>What a failed delivery report said, e.g. "Delivery report part 1/2: status 65 (3gpp)".</summary>
    public static string DescribeReportFailure(int tpStatus, string? format, int part, int parts)
    {
        var text = $"status {tpStatus}";
        if (!string.IsNullOrEmpty(format))
            text += $" ({format})";
        return Prefix(part, parts, "Delivery report ") + text;
    }

    private static string Prefix(int part, int parts, string lead)
    {
        var label = parts > 1 && part >= 1 ? $"{lead}part {part}/{parts}" : lead.TrimEnd();
        return label.Length == 0 ? string.Empty : char.ToUpperInvariant(label[0]) + label[1..] + ": ";
    }

    /// <summary>
    /// Keeps a list of row ids in the order they were added, at most <paramref name="cap"/> of them, as a comma
    /// separated string. Returns the new list; <paramref name="evicted"/> are the ids that no longer fit.
    /// </summary>
    public static string Remember(string? order, long rowId, int cap, out IReadOnlyList<long> evicted)
    {
        var ids = (order ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => long.TryParse(s, out var id) ? id : 0)
            .Where(id => id > 0 && id != rowId)
            .ToList();
        ids.Add(rowId);

        var over = Math.Max(0, ids.Count - Math.Max(1, cap));
        evicted = ids.Take(over).ToList();
        return string.Join(',', ids.Skip(over));
    }
}
