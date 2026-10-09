using Android.Content;

namespace Gheychi.App.Platforms.Android.Notifications;

/// <summary>
/// What another app asked for: a conversation with a number (a contact's "message" button, an sms: link), a text to
/// send somewhere (the share sheet), or both (a number and the text to start it with).
/// </summary>
public sealed record ExternalSendRequest(string? Address, string? Text)
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

        return string.IsNullOrWhiteSpace(address) && string.IsNullOrEmpty(text)
            ? null
            : new ExternalSendRequest(address?.Trim(), text);
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
        return string.IsNullOrEmpty(text) && string.IsNullOrWhiteSpace(address)
            ? null
            : new ExternalSendRequest(address, text);
    }
}
