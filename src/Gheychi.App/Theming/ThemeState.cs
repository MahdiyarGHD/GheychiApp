using System.Globalization;
using Android.Content.Res;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Gheychi.App;

public enum AppTheme
{
    Unspecified,
    Light,
    Dark
}

/// <summary>
/// The theme is fixed for the life of the process: choosing one in Settings restarts the app. That lets every
/// themed colour be resolved once, when its XAML loads, instead of tracking the theme through a binding.
/// </summary>
public static class ThemeState
{
    public static bool IsDark { get; private set; }

    public static void Resolve()
    {
        IsDark = Services.AppPreferences.Theme switch
        {
            AppTheme.Light => false,
            AppTheme.Dark => true,
            _ => SystemIsDark()
        };
    }

    public static bool SystemIsDark() =>
        (Platform.AppContext.Resources?.Configuration?.UiMode & UiMode.NightMask) == UiMode.NightYes;
}

/// <summary>Frozen brushes shared by everything that uses the same colour; a view model never allocates one per row.</summary>
public static class Palette
{
    private static readonly Dictionary<string, IImmutableBrush> Brushes = new(StringComparer.OrdinalIgnoreCase);

    public static IImmutableBrush Brush(string argb)
    {
        lock (Brushes)
        {
            if (!Brushes.TryGetValue(argb, out var brush))
                Brushes[argb] = brush = new ImmutableSolidColorBrush(Color.Parse(argb));
            return brush;
        }
    }

    public static IImmutableBrush Pick(string light, string dark) => Brush(ThemeState.IsDark ? dark : light);

    public static IImmutableBrush Transparent { get; } = new ImmutableSolidColorBrush(Colors.Transparent);
}

public static class AppFonts
{
    private const string Folder = "avares://Gheychi.App/UiAssets/Fonts#";

    private static readonly bool Persian =
        CultureInfo.CurrentUICulture.Name.StartsWith("fa", StringComparison.OrdinalIgnoreCase);

    public static FontFamily Regular { get; } = new(Folder + (Persian ? "Vazirmatn" : "Plus Jakarta Sans"));

    public static FontFamily Bold { get; } = new(Folder + (Persian ? "Vazirmatn SemiBold" : "Plus Jakarta Sans SemiBold"));

    /// <summary>
    /// A 13sp digit centred by its line box is not centred by its ink: from the fonts' metrics, Plus Jakarta Sans
    /// digits sit 0.12em below the middle and Vazirmatn digits 0.088em above it. Used to centre the SIM badge digit.
    /// </summary>
    public static double BadgeDigitOffsetY => Persian ? 1.1 : -1.6;
}
