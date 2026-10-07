using System.Globalization;
using Gheychi.App.Localization;
using Gheychi.Core.Services;

namespace Gheychi.App.Controls.Settings;

internal static class SettingsUi
{
    public static bool IsPersian => CultureInfo.CurrentUICulture.Name.StartsWith("fa", StringComparison.OrdinalIgnoreCase);

    public static bool IsRightToLeft => CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft;

    public static string Digits(string text) => IsPersian ? DateFormattingService.ToPersianDigits(text) : text;

    public static string Number(int value) => Digits(value.ToString(CultureInfo.InvariantCulture));

    public static string Days(int days) => string.Format(LocalizationManager.Instance["Settings_Days"], Number(days));

    public static string Megabytes(long bytes) =>
        string.Format(LocalizationManager.Instance["Settings_Megabytes"], Digits((bytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture)));

    public static void MirrorChevrons(Element root, ResourceDictionary resources)
    {
        if (!IsRightToLeft || !resources.TryGetValue("Chevron", out var style))
            return;

        foreach (var image in root.GetVisualTreeDescendants().OfType<Image>().Where(i => i.Style == style))
            image.Rotation = 180;
    }

    /// <summary>Marks one segment of a segmented choice as picked; the styles carry the theme colors.</summary>
    public static void Select(ResourceDictionary resources, IEnumerable<Border> segments, Border? selected)
    {
        foreach (var segment in segments)
        {
            var on = segment == selected;
            segment.Style = (Style)resources[on ? "SegmentOn" : "SegmentOff"];
            var labels = segment.Content switch
            {
                Layout layout => layout.Children.OfType<Label>().ToList(),
                Label label => [label],
                _ => []
            };
            for (var i = 0; i < labels.Count; i++)
                labels[i].Style = (Style)resources[i == 0 ? on ? "SegmentTitleOn" : "SegmentTitleOff" : on ? "SegmentHintOn" : "SegmentHintOff"];
        }
    }
}
