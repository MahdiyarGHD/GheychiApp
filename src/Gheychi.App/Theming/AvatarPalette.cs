using Avalonia.Media;
using Gheychi.Core.Services;

namespace Gheychi.App.Theming;

/// <summary>
/// Hand-tuned pastel avatar pairs (soft tint + high-contrast ink) matching the app's light/dark accent tones,
/// picked deterministically per contact or number so fallback avatars keep the same colour across every screen.
/// </summary>
public static class AvatarPalette
{
    private readonly record struct Tone(string BgLight, string FgLight, string BgDark, string FgDark);

    private static readonly Tone[] Tones =
    [
        new("#DCEFE3", "#1B5E43", "#1F4A35", "#6FD3A8"), // green
        new("#DCEFEE", "#1C5C5A", "#204948", "#6FD0CC"), // teal
        new("#D9E4F2", "#17519E", "#173152", "#A0C3EF"), // blue
        new("#DBDBF0", "#363CD2", "#1C1D4D", "#BBBCEF"), // indigo
        new("#E6DCEF", "#732EAE", "#361E4B", "#D2B6EA"), // purple
        new("#F1DAE6", "#9B1F5D", "#4E1B35", "#EDAECE"), // pink
        new("#F1DADB", "#A11D26", "#4F1A1D", "#EFB1B5"), // red
        new("#F6E1D5", "#903704", "#592B10", "#F9AF85"), // orange
        new("#F7EAD4", "#754A00", "#5B3F0E", "#FBB336"), // amber
    ];

    public static (IBrush Background, IBrush Foreground) For(string? address, string? name = null)
    {
        var key = KeyFor(address, name);
        if (key.Length == 0)
            return (Palette.Accent(AccentRole.Soft), Palette.Accent(AccentRole.Text));

        var index = (int)(Hash(key) % (uint)Tones.Length);
        var tone = Tones[index];
        return (Palette.Pick(tone.BgLight, tone.BgDark), Palette.Pick(tone.FgLight, tone.FgDark));
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
