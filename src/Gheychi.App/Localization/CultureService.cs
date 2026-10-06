using System.Globalization;

namespace Gheychi.App.Localization;

public static class CultureService
{
    /// <summary>The language picked in Settings, else the phone's.</summary>
    public static void ApplyCulture()
    {
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
