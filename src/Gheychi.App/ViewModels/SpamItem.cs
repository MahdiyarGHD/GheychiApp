using System.Globalization;
using Gheychi.App.Localization;
using Gheychi.Core.Services;
using Gheychi.Core.Spam;

namespace Gheychi.App.ViewModels;

public sealed class SpamItem
{
    public static readonly Color VeryLikelyColor = Color.FromArgb("#D64545");
    public static readonly Color LikelyColor = Color.FromArgb("#C7841A");

    public required SpamMessage Message { get; init; }
    public long Id => Message.Id;
    public required string Sender { get; init; }
    public required string Confidence { get; init; }
    public required string Time { get; init; }
    public string Preview => Message.Body;

    public bool IsVeryLikely => SpamConfidence.Level(Message.Score) == SpamConfidenceLevel.VeryLikely;
    public Color ConfidenceColor => IsVeryLikely ? VeryLikelyColor : LikelyColor;
    public string ScoreText => FormatPercent(SpamConfidence.Percent(Message.Score));

    /// <summary>"85%" or, in Persian, "۸۵٪".</summary>
    public static string FormatPercent(int percent)
    {
        var digits = percent.ToString(CultureInfo.InvariantCulture);
        if (CultureInfo.CurrentUICulture.Name.StartsWith("fa", StringComparison.OrdinalIgnoreCase))
            digits = DateFormattingService.ToPersianDigits(digits);
        return string.Format(LocalizationManager.Instance["Spam_InfoPercent"], digits);
    }
}
