using System.Globalization;

namespace Gheychi.App.Localization;

public static class CultureService
{
    public static void ApplySystemCulture()
    {
        var culture = CultureInfo.CurrentUICulture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    public static FlowDirection GetFlowDirection() =>
        CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
}
