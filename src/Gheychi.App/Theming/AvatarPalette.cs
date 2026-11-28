using Avalonia.Media;
using Gheychi.Core.Services;

namespace Gheychi.App.Theming;

/// <summary>
/// Google-Messages-style contact avatar fills paired with a cutout foreground matching the list surface
/// background (<c>#FFFFFF</c> in light mode, <c>#1E1F22</c> in dark mode), picked deterministically per
/// contact or number across every screen.
/// </summary>
public static class AvatarPalette
{
    private readonly record struct Tone(string BgLight, string BgDark);

    private static readonly Tone[] Tones =
    [
        new("#1A73E8", "#7CACF8"), // blue
        new("#1E8E3E", "#6DD58C"), // green
        new("#00897B", "#5BB9B0"), // teal
        new("#3F51B5", "#9DA8FF"), // indigo
        new("#9334E6", "#C58AF9"), // purple
        new("#E52592", "#F48FB1"), // pink
        new("#D93025", "#F28B82"), // red
        new("#E8710A", "#FCAD70"), // orange
        new("#0288D1", "#4FC3F7"), // cyan
    ];

    /// <summary>Glyph/icon tint matching the rounded list surface so initials, icons and checkmarks read as clean cutouts.</summary>
    public static IBrush CutoutForeground { get; } = Palette.Pick("#FFFFFF", "#1E1F22");

    /// <summary>Selected-avatar background tuned to keep high contrast with <see cref="CutoutForeground"/> in both themes.</summary>
    public static IBrush SelectionAccentFill =>
        Palette.Pick(ThemeState.Accent.Hex(AccentRole.Solid, false), ThemeState.Accent.Hex(AccentRole.Glow, true));

    public static (IBrush Background, IBrush Foreground) For(string? address, string? name = null)
    {
        var key = KeyFor(address, name);
        if (key.Length == 0)
            return (SelectionAccentFill, CutoutForeground);

        var index = (int)(Hash(key) % (uint)Tones.Length);
        var tone = Tones[index];
        return (Palette.Pick(tone.BgLight, tone.BgDark), CutoutForeground);
    }

    private static string KeyFor(string? address, string? name)
    {
        if (!string.IsNullOrWhiteSpace(address))
        {
            var normalized = PhoneNumberNormalizer.ToLookupKey(address);
            if (normalized.Length > 0)
                return normalized;
            return address.Trim();
        }

        return string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();
    }

    // FNV-1a: deterministic across launches (string.GetHashCode is randomized per process).
    private static uint Hash(string text)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var ch in text)
            {
                hash ^= char.ToUpperInvariant(ch);
                hash *= 16777619u;
            }
            return hash;
        }
    }
}
