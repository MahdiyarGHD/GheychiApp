using Android.Graphics.Fonts;
using Avalonia.Media;
using Avalonia.Media.Fonts;

namespace Gheychi.App.Platforms.Android;

/// <summary>
/// Android updates its emoji font (new emoji such as the phoenix, which is a bird and a flame joined) through Play,
/// into /data/fonts. The text engine only knows the copy on the system partition, so the new sequences show as their
/// parts. The updated file is handed to the engine as the font to try first for emoji.
/// </summary>
internal static class SystemEmoji
{
    private const string Family = "Noto Color Emoji";
    private static readonly Uri CollectionKey = new("fonts:SystemEmoji");
    private const string PathKey = "system_emoji_font_v1";

    // Looking the file up opens every font of the phone, which held up each launch (and each SMS that started the
    // process) before the first frame. The path found by the last look is used instead, and looked up again after start.
    private static readonly TimeSpan RescanDelay = TimeSpan.FromSeconds(15);

    /// <summary>Null when the phone has no updated emoji font, and the system's own is the newest there is.</summary>
    private static readonly string? UpdatedFontPath = Remembered();

    public static FontFallback[]? Fallbacks => UpdatedFontPath is null
        ? null
        : new[]
        {
            new FontFallback
            {
                FontFamily = new Avalonia.Media.FontFamily($"{CollectionKey.OriginalString}#{Family}"),
                // Not the flag letters (1F1E6-1F1FF): the flags are a font of their own.
                UnicodeRange = UnicodeRange.Parse("1F000-1F1E5,1F200-1FAFF,2600-27BF,2B00-2BFF")
            }
        };

    /// <summary>Reads the file into the engine; call once the platform is set up.</summary>
    public static void Register()
    {
        if (UpdatedFontPath is not null)
            FontManager.Current.AddFontCollection(new EmbeddedFontCollection(CollectionKey, new Uri("file://" + UpdatedFontPath)));
    }

    // An emoji update through Play removes the old file, so a path that is gone counts as none until the next look.
    private static string? Remembered()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(29))
            return null;

        _ = Task.Run(async () =>
        {
            await Task.Delay(RescanDelay);
            Preferences.Default.Set(PathKey, Find() ?? string.Empty);
        });

        var path = Preferences.Default.Get(PathKey, string.Empty);
        return path.Length > 0 && File.Exists(path) ? path : null;
    }

    private static string? Find()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(29))
            return null;

        try
        {
            foreach (var font in SystemFonts.AvailableFonts ?? [])
            {
                var path = font.File?.AbsolutePath;
                if (path is not null && path.StartsWith("/data/", StringComparison.Ordinal) &&
                    path.Contains("emoji", StringComparison.OrdinalIgnoreCase) && !path.Contains("flags", StringComparison.OrdinalIgnoreCase))
                    return path;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Finding the system emoji font failed: {ex}");
        }

        return null;
    }
}
