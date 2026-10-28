using System.Globalization;
using Avalonia.Media;

namespace Gheychi.App.Localization;

public static class CultureService
{
    /// <summary>The language picked in Settings, else the phone's, and the calendar picked there.</summary>
    public static void ApplyCulture()
    {
        Gheychi.Core.Services.DateFormattingService.Calendar = Services.AppPreferences.Calendar;

        var culture = Services.AppPreferences.Language switch
        {
            Services.AppPreferences.LanguageEnglish => new CultureInfo("en-US"),
            Services.AppPreferences.LanguagePersian => new CultureInfo("fa-IR"),
            _ => CultureInfo.CurrentUICulture
        };
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    public static FlowDirection GetFlowDirection() =>
        CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
}
