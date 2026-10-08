using System.Globalization;
using Avalonia.Controls;
using Gheychi.App.Localization;

namespace Gheychi.App.Controls.Settings;

internal static class SettingsUi
{
    public static bool IsPersian => LocalizedNumbers.IsPersian;

    public static string Digits(string text) => LocalizedNumbers.Digits(text);

    public static string Number(int value) => LocalizedNumbers.Number(value);

    public static string Days(int days) => string.Format(LocalizationManager.Instance["Settings_Days"], Number(days));

    public static string Megabytes(long bytes) =>
        string.Format(LocalizationManager.Instance["Settings_Megabytes"], Digits((bytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture)));

    /// <summary>Marks one segment of a segmented choice as picked; the styles carry the theme colors.</summary>
    public static void Select(IEnumerable<Border> segments, Border? selected)
    {
        foreach (var segment in segments)
            segment.Classes.Set("on", segment == selected);
    }
}

/// <summary><c>{settings:UpperTranslate Key}</c>: the text in the language in use, in capitals (section titles).</summary>
public sealed class UpperTranslateExtension
{
    public UpperTranslateExtension()
    {
    }

    public UpperTranslateExtension(string key) => Key = key;

    public string Key { get; set; } = string.Empty;

    public string ProvideValue(IServiceProvider? serviceProvider = null) =>
        LocalizationManager.Instance[Key].ToUpper(CultureInfo.CurrentUICulture);
}
