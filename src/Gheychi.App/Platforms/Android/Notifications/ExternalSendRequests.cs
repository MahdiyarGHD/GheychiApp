using Android.Content;
using Android.Telecom;

namespace Gheychi.App.Platforms.Android.Notifications;

/// <summary>
/// What another app asked for: a conversation with a number (a contact's "message" button, an sms: link), a text to
/// send somewhere (the share sheet), or both (a number and the text to start it with), plus any SIM hint passed by the dialer.
/// </summary>
public sealed record ExternalSendRequest(
    string? Address,
    string? Text,
    int SubId = 0,
    int SlotIndex = 0,
    string? PhoneAccountId = null)
{
    public bool HasAddress => !string.IsNullOrWhiteSpace(Address);
}

/// <summary>
/// Kept until the inbox takes it, like <see cref="ChatLaunchRequests"/>: on a cold start the activity receives the
/// intent before any page exists, and from the background it arrives before the activity is visible.
/// </summary>
public static class ExternalSendRequests
{
    private static readonly string[] SmsSchemes = ["sms", "smsto", "mms", "mmsto"];

    private static ExternalSendRequest? _pending;

    public static event Action? Requested;

    public static bool HasPending => Volatile.Read(ref _pending) is not null;

    public static bool HasPendingAddress => Volatile.Read(ref _pending)?.HasAddress == true;

    public static void FromIntent(Intent? intent)
    {
        // An activity brought back from the recent apps gets its old intent again; that is not a new request.
        if (intent is null || (intent.Flags & ActivityFlags.LaunchedFromHistory) != 0 || intent.GetBooleanExtra(HandledExtra, false))
            return;

        var request = intent.Action switch
        {
            Intent.ActionSendto or Intent.ActionView => FromSmsUri(intent),
            Intent.ActionSend => FromShare(intent),
            _ => null
        };

        if (request is null)
            return;

        intent.PutExtra(HandledExtra, true);
        Volatile.Write(ref _pending, request);

        if (ChatPresence.IsAppVisible)
            Requested?.Invoke();
    }

    /// <summary>Called once the activity is visible again, so a request that arrived on the way is acted on.</summary>
    public static void RaiseIfPending()
    {
        if (Volatile.Read(ref _pending) is not null)
            Requested?.Invoke();
    }

    /// <summary>The pending request, if any; each request is handed out once.</summary>
    public static ExternalSendRequest? Take() => Interlocked.Exchange(ref _pending, null);

    private const string HandledExtra = "gheychi.external_handled";

    // "smsto:+98912...?body=Hi", "sms:+98912...;+98935..." (a group: the first one), with the text also given as "sms_body".
    private static ExternalSendRequest? FromSmsUri(Intent intent)
    {
        var data = intent.Data;
        if (data?.Scheme is not { } scheme || !SmsSchemes.Contains(scheme, StringComparer.OrdinalIgnoreCase))
            return null;

        var specific = data.SchemeSpecificPart ?? string.Empty;
        var query = string.Empty;
        var at = specific.IndexOf('?');
        if (at >= 0)
        {
            query = specific[(at + 1)..];
            specific = specific[..at];
        }

        var address = specific.TrimStart('/').Split(',', ';').Select(Uri.UnescapeDataString).FirstOrDefault(a => !string.IsNullOrWhiteSpace(a));
        var text = intent.GetCharSequenceExtra("sms_body")?.ToString() ?? intent.GetCharSequenceExtra(Intent.ExtraText)?.ToString() ?? BodyFrom(query);
        var (subId, slotIndex, accountId) = ReadSimHint(intent, query);

        return string.IsNullOrWhiteSpace(address) && string.IsNullOrEmpty(text)
            ? null
            : new ExternalSendRequest(address?.Trim(), text, subId, slotIndex, accountId);
    }

    private static string? BodyFrom(string query)
    {
        foreach (var pair in query.Split('&'))
        {
            var equals = pair.IndexOf('=');
            if (equals > 0 && pair[..equals].Equals("body", StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(pair[(equals + 1)..].Replace('+', ' '));
        }

        return null;
    }

    private static ExternalSendRequest? FromShare(Intent intent)
    {
        var text = intent.GetCharSequenceExtra(Intent.ExtraText)?.ToString();
        if (string.IsNullOrEmpty(text))
            text = intent.GetCharSequenceExtra(Intent.ExtraSubject)?.ToString();

        var address = intent.GetStringExtra("address");
        var (subId, slotIndex, accountId) = ReadSimHint(intent, string.Empty);
        return string.IsNullOrEmpty(text) && string.IsNullOrWhiteSpace(address)
            ? null
            : new ExternalSendRequest(address, text, subId, slotIndex, accountId);
    }

    private static readonly string[] SubIdExtraKeys =
    [
        "android.telephony.extra.SUBSCRIPTION_INDEX",
        "subscription",
        "subscription_id",
        "sub_id"
    ];

    private static readonly string[] ZeroBasedSlotExtraKeys =
    [
        "com.android.phone.extra.slot",
        "slot",
        "slot_id",
        "sim_id",
        "simSlot",
        "sim_slot"
    ];

    private static (int SubId, int SlotIndex, string? PhoneAccountId) ReadSimHint(Intent intent, string query)
    {
        var subId = 0;
        foreach (var key in SubIdExtraKeys)
        {
            var candidate = ReadIntExtra(intent, key);
            if (candidate is > 0)
            {
                subId = candidate.Value;
                break;
            }
        }

        if (subId <= 0)
        {
            foreach (var key in new[] { "sub_id", "subscription", "subscription_id" })
            {
                if (QueryInt(query, key) is > 0 and var querySubId)
                {
                    subId = querySubId;
                    break;
                }
            }
        }

        var slotIndex = 0;
        foreach (var key in ZeroBasedSlotExtraKeys)
        {
            var candidate = ReadIntExtra(intent, key);
            if (candidate is >= 0 and var rawSlot)
            {
                slotIndex = rawSlot + 1;
                break;
            }
        }

        string? accountId = null;
        try
        {
#pragma warning disable CA1416
            PhoneAccountHandle? handle = OperatingSystem.IsAndroidVersionAtLeast(33)
                ? intent.GetParcelableExtra("android.telecom.extra.PHONE_ACCOUNT_HANDLE", Java.Lang.Class.FromType(typeof(PhoneAccountHandle))) as PhoneAccountHandle
#pragma warning disable CA1422
                : intent.GetParcelableExtra("android.telecom.extra.PHONE_ACCOUNT_HANDLE") as PhoneAccountHandle;
#pragma warning restore CA1422
#pragma warning restore CA1416
            accountId = handle?.Id;
        }
        catch
        {
        }

        return (subId, slotIndex, accountId);
    }

    private static int? ReadIntExtra(Intent intent, string key)
    {
        if (!intent.HasExtra(key))
            return null;

        var intValue = intent.GetIntExtra(key, -1);
        if (intValue >= 0)
            return intValue;

        var longValue = intent.GetLongExtra(key, -1L);
        if (longValue is >= 0 and <= int.MaxValue)
            return (int)longValue;

        return int.TryParse(intent.GetStringExtra(key), out var parsed) && parsed >= 0 ? parsed : null;
    }

    private static int? QueryInt(string query, string key)
    {
        if (string.IsNullOrEmpty(query))
            return null;

        foreach (var pair in query.Split('&'))
        {
            var equals = pair.IndexOf('=');
            if (equals > 0 && pair[..equals].Equals(key, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(Uri.UnescapeDataString(pair[(equals + 1)..]), out var value))
                return value;
        }

        return null;
    }
}
