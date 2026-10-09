using Avalonia.Media;

namespace Gheychi.App.Theming;

/// <summary>
/// <c>{theme:ThemeBrush Light=#1B1E24, Dark=#E8EAED}</c>: the brush for the theme in use. The ported form of MAUI's
/// <c>AppThemeBinding</c>, resolved once at load because the theme cannot change while the app runs.
/// </summary>
public sealed class ThemeBrushExtension
{
    public ThemeBrushExtension()
    {
    }

    public ThemeBrushExtension(string light, string dark)
    {
        Light = light;
        Dark = dark;
    }

    public string Light { get; set; } = "#00000000";

    public string? Dark { get; set; }

    public IBrush ProvideValue(IServiceProvider? serviceProvider = null) => Palette.Pick(Light, Dark ?? Light);
}

/// <summary>
/// <c>{theme:AccentBrush Role=Text}</c>: the brush of the chosen accent colour for a role. <c>Light</c> and <c>Dark</c>
/// replace the accent in one theme where the other theme keeps a neutral.
/// </summary>
public sealed class AccentBrushExtension
{
    public AccentRole Role { get; set; }

    public string? Light { get; set; }

    public string? Dark { get; set; }

    /// <summary>0-255; the accent's opacity, for hairlines and highlights.</summary>
    public byte Alpha { get; set; } = 255;

    public IBrush ProvideValue(IServiceProvider? serviceProvider = null) =>
        (ThemeState.IsDark ? Dark : Light) is { } neutral ? Palette.Brush(neutral)
        : Alpha == 255 ? Palette.Accent(Role) : Palette.Accent(Role, Alpha);
}

/// <summary>Same as <see cref="ThemeBrushExtension"/> for properties that take a <see cref="Color"/>.</summary>
public sealed class ThemeColorExtension
{
    public string Light { get; set; } = "#00000000";

    public string? Dark { get; set; }

    public Color ProvideValue(IServiceProvider? serviceProvider = null) =>
        Color.Parse(ThemeState.IsDark ? Dark ?? Light : Light);
}
