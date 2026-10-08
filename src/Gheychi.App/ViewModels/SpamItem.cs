using System.Globalization;
using Avalonia.Media;
using Gheychi.App.Localization;
using Gheychi.Core.Services;
using Gheychi.Core.Spam;

namespace Gheychi.App.ViewModels;

public sealed class SpamItem
{
    public static readonly IBrush VeryLikelyColor = Palette.Brush("#D64545");
    public static readonly IBrush LikelyColor = Palette.Brush("#C7841A");

    public required SpamMessage Message { get; init; }
    public long Id => Message.Id;
    public required string Sender { get; init; }
    public required string Confidence { get; init; }
    public required string Time { get; init; }
    /// <summary>Time of day alone, for the Spam tab where the day is in the header above the row.</summary>
    public required string Clock { get; init; }
    public string Preview => Message.Body;

    public bool IsVeryLikely => SpamConfidence.Level(Message.Score) == SpamConfidenceLevel.VeryLikely;
    public IBrush ConfidenceColor => IsVeryLikely ? VeryLikelyColor : LikelyColor;
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
