using Gheychi.Core.Spam;

namespace Gheychi.Infrastructure.Spam;

/// <summary>
/// Reports go to the dataset's Google Form, posted the way the form's own page submits it. The text is sent
/// normalized, as the trainer and the app's classifier see it, so numbers and links never leave the phone.
/// </summary>
public sealed class GoogleFormSpamReporter : ISpamReporter
{
    public const string FormResponseUrl =
        "https://docs.google.com/forms/d/e/1FAIpQLSfmrNaM-SWiQd17DYbFGVt9FWEW6RJcgh0b9znRnY_QqGcsow/formResponse";

    // The form's answer values, exactly as its choices are written.
    public const string SpamLabel = "هرزنامه";
    public const string HamLabel = "پیام معمولی";

    private const string LabelField = "entry.1968518309";
    private const string TextField = "entry.2103089956";

    private readonly HttpClient _http;

    public GoogleFormSpamReporter(HttpClient http)
    {
        _http = http;
    }

    /// <summary>The form fields for a report; null when the text normalizes to nothing.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>>? Fields(string text, bool isSpam)
    {
        var normalized = TextNormalizer.Normalize(text);
        if (normalized.Length == 0)
            return null;

        return
        [
            new(LabelField, isSpam ? SpamLabel : HamLabel),
            new(TextField, normalized)
        ];
    }

    public async Task ReportAsync(string text, bool isSpam, CancellationToken cancellationToken = default)
    {
        if (Fields(text, isSpam) is not { } fields)
            return;

        using var content = new FormUrlEncodedContent(fields);
        using var response = await _http.PostAsync(FormResponseUrl, content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
