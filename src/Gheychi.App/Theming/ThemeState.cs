using System.Globalization;
using Android.Content.Res;
using AndroidX.AppCompat.App;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Media.Immutable;
using Gheychi.App.Theming;

namespace Gheychi.App;

public enum AppTheme
{
    Unspecified,
    Light,
    Dark
}

/// <summary>
/// The theme and the accent colour are fixed for the life of the process: choosing either in Settings restarts the
/// app. That lets every themed colour be resolved once, when its XAML loads, instead of tracking it through a binding.
/// </summary>
public static class ThemeState
{
    public static bool IsDark { get; private set; }

    public static AccentScheme Accent { get; private set; } = AccentScheme.Default;

    public static void Resolve()
    {
        IsDark = Services.AppPreferences.Theme switch
        {
            AppTheme.Light => false,
            AppTheme.Dark => true,
            _ => SystemIsDark()
        };
        Accent = AccentScheme.Find(Services.AppPreferences.Accent);
        TextScale.Resolve();
    }

    public static bool SystemIsDark() =>
        (Platform.AppContext.Resources?.Configuration?.UiMode & UiMode.NightMask) == UiMode.NightYes;

    /// <summary>Tells Android the theme picked in the app, for what it draws itself: dialogs, pickers, the launch window.</summary>
    public static void ApplyToSystem(AppTheme theme)
    {
        AppCompatDelegate.DefaultNightMode = theme switch
        {
            AppTheme.Light => AppCompatDelegate.ModeNightNo,
            AppTheme.Dark => AppCompatDelegate.ModeNightYes,
            _ => AppCompatDelegate.ModeNightFollowSystem
        };

        // From Android 12 the splash is drawn by the system before any app code runs. The system keeps this mode and
        // picks the light or dark launch resources by it, so the splash follows the app's theme, not only the phone's.
        // It is also what the next process starts with, so it is set before a restart for the new theme.
        if (OperatingSystem.IsAndroidVersionAtLeast(31) && Platform.AppContext.GetSystemService(global::Android.Content.Context.UiModeService) is global::Android.App.UiModeManager uiMode)
        {
            uiMode.SetApplicationNightMode((int)(theme switch
            {
                AppTheme.Light => global::Android.App.UiNightMode.No,
                AppTheme.Dark => global::Android.App.UiNightMode.Yes,
                _ => global::Android.App.UiNightMode.Auto
            }));
        }
    }
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

    public static IImmutableBrush Accent(AccentRole role) => Brush(ThemeState.Accent.Hex(role, ThemeState.IsDark));

    public static IImmutableBrush Accent(AccentRole role, byte alpha) =>
        Brush("#" + alpha.ToString("X2") + ThemeState.Accent.Hex(role, ThemeState.IsDark)[1..]);

    public static IImmutableBrush Transparent { get; } = new ImmutableSolidColorBrush(Colors.Transparent);
}

public static class AppFonts
{
    private const string Folder = "avares://Gheychi.App/UiAssets/Fonts#";

    private static readonly bool Persian =
        CultureInfo.CurrentUICulture.Name.StartsWith("fa", StringComparison.OrdinalIgnoreCase);

    // In Persian the UI font is Plus Jakarta Sans for Latin text over Vazirmatn for Persian text, one file per weight
    // (tools/merge_fonts.py): a fallback would draw bold Persian in Regular.
    public static FontFamily Regular { get; } = new(Folder + (Persian ? "Gheychi Fa" : "Plus Jakarta Sans"));

    public static FontFamily Bold { get; } = new(Folder + (Persian ? "Gheychi Fa SemiBold" : "Plus Jakarta Sans SemiBold"));

    /// <summary>The text of a message: Vazirmatn alone in Persian, so a Latin word in a Persian message keeps its look.</summary>
    public static FontFamily Message { get; } = Persian ? new(Folder + "Vazirmatn") : Regular;

    public static FontFamily MessageBold { get; } = Persian ? new(Folder + "Vazirmatn SemiBold") : Bold;

    /// <summary>
    /// Persian letters in the English UI come from Vazirmatn, not the phone's own font. A list of font families would
    /// do it, but not with the SemiBold family: the whole list then falls back to the system font.
    /// </summary>
    public static FontFallback? PersianLetters => Persian
        ? null
        : new FontFallback
        {
            FontFamily = new FontFamily(Folder + "Vazirmatn"),
            UnicodeRange = UnicodeRange.Parse("0600-06FF,0750-077F,08A0-08FF,FB50-FDFF,FE70-FEFF")
        };

    /// <summary>
    /// A 13sp digit centred by its line box is not centred by its ink: from the fonts' metrics, Plus Jakarta Sans
    /// digits sit 0.12em below the middle and Vazirmatn digits 0.088em above it. Used to centre the SIM badge digit.
    /// </summary>
    public static double BadgeDigitOffsetY => Persian ? 1.1 : -1.6;
}
