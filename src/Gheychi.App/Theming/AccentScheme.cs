namespace Gheychi.App.Theming;

/// <summary>The places the accent colour is used; a scheme gives each one a value per theme.</summary>
public enum AccentRole
{
    /// <summary>Fills that carry white content: sent bubbles, send button, switches, the selected segment's border.</summary>
    Solid,

    /// <summary>Accent text and glyphs on a neutral surface; deep in light, bright in dark.</summary>
    Text,

    /// <summary>Accent glyphs and text that read well a shade lighter: the Solid colour in light, bright in dark.</summary>
    Icon,

    /// <summary>The tint behind avatars, tab pills and badges.</summary>
    Soft,

    /// <summary>A stronger tint than <see cref="Soft"/>, for badges sitting on a card.</summary>
    Strong,

    /// <summary>The faintest tint: chips and highlight cards.</summary>
    Wash,

    /// <summary>A mid tone, for secondary chart bars.</summary>
    Mid,

    /// <summary>A dark tile in either theme, for the app logo.</summary>
    Deep,

    /// <summary>The bright tone, for glyphs on Solid or Deep in either theme.</summary>
    Glow,

    /// <summary>Muted text on a Solid fill: message time, sending spinner.</summary>
    OnSolid,

    /// <summary>Brighter text on a Solid fill: links.</summary>
    OnSolidBright,

    /// <summary>The fill of a sent message: a light tone of the accent in both themes, under dark text.</summary>
    Bubble,

    /// <summary>Text on a <see cref="Bubble"/>: near black in both themes.</summary>
    OnBubble,

    /// <summary>Quieter text and glyphs on a <see cref="Bubble"/>: message time, sending spinner, ticks.</summary>
    OnBubbleMuted,

    /// <summary>Links on a <see cref="Bubble"/>: the accent, darkened until it reads.</summary>
    OnBubbleLink
}

/// <summary>
/// One accent colour of the app, as a hand-tuned set of tones. Every scheme keeps the same contrast against white
/// (Solid), against the light and dark surfaces (Text) and against its own fill (OnSolid), so switching the accent
/// never costs legibility. Green is the original look.
/// </summary>
public sealed class AccentScheme
{
    private static readonly AccentScheme[] Presets =
    [
        new("green", "Accent_Green",
            solid: "#2E7D5B", ink: "#1B5E43", glow: "#6FD3A8",
            softLight: "#DCEFE3", softDark: "#1F4A35", strongLight: "#B9EBCB", strongDark: "#1F5A3F",
            washLight: "#DDF0E4", washDark: "#173527", midLight: "#6FB592", midDark: "#3F8566",
            deep: "#16382A", onSolid: "#BFE8D2", onSolidBright: "#E3F8EC"),
        new("teal", "Accent_Teal",
            solid: "#307B78", ink: "#1C5C5A", glow: "#6FD0CC",
            softLight: "#DCEFEE", softDark: "#204948", strongLight: "#BAEAE8", strongDark: "#205957",
            washLight: "#DDF0EF", washDark: "#183433", midLight: "#6FB2B0", midDark: "#408280",
            deep: "#173736", onSolid: "#BFE7E5", onSolidBright: "#E4F7F7"),
        new("blue", "Accent_Blue",
            solid: "#2E6FC5", ink: "#17519E", glow: "#A0C3EF",
            softLight: "#D9E4F2", softDark: "#173152", strongLight: "#B0CDF4", strongDark: "#153764",
            washLight: "#DAE5F3", washDark: "#12233A", midLight: "#8BAAD1", midDark: "#4579BE",
            deep: "#10243E", onSolid: "#D0E0F5", onSolidBright: "#DFECFC"),
        new("indigo", "Accent_Indigo",
            solid: "#6064CE", ink: "#363CD2", glow: "#BBBCEF",
            softLight: "#DBDBF0", softDark: "#1C1D4D", strongLight: "#B5B7EF", strongDark: "#1B1D5E",
            washLight: "#DCDCF1", washDark: "#151637", midLight: "#A1A3D4", midDark: "#6E70C2",
            deep: "#13153B", onSolid: "#DDDDF5", onSolidBright: "#E1E2FA"),
        new("purple", "Accent_Purple",
            solid: "#8F53C4", ink: "#732EAE", glow: "#D2B6EA",
            softLight: "#E6DCEF", softDark: "#361E4B", strongLight: "#D4B8EC", strongDark: "#3F1E5B",
            washLight: "#E7DDF0", washDark: "#271636", midLight: "#B79CCE", midDark: "#9164B9",
            deep: "#281539", onSolid: "#E7D9F2", onSolidBright: "#EEE2F9"),
        new("pink", "Accent_Pink",
            solid: "#C4387E", ink: "#9B1F5D", glow: "#EDAECE",
            softLight: "#F1DAE6", softDark: "#4E1B35", strongLight: "#F0B4D2", strongDark: "#60193D",
            washLight: "#F2DBE6", washDark: "#381426", midLight: "#D196B3", midDark: "#BB5588",
            deep: "#3B1327", onSolid: "#F4D7E6", onSolidBright: "#FAE1EE"),
        new("red", "Accent_Red",
            solid: "#CA3B44", ink: "#A11D26", glow: "#EFB1B5",
            softLight: "#F1DADB", softDark: "#4F1A1D", strongLight: "#F1B3B7", strongDark: "#61181D",
            washLight: "#F2DBDC", washDark: "#391316", midLight: "#D3979B", midDark: "#BF585E",
            deep: "#3C1215", onSolid: "#F5D8DA", onSolidBright: "#FBE0E2"),
        new("orange", "Accent_Orange",
            solid: "#B65116", ink: "#903704", glow: "#F9AF85",
            softLight: "#F6E1D5", softDark: "#592B10", strongLight: "#FCC7A8", strongDark: "#6F2F0A",
            washLight: "#F7E2D6", washDark: "#401F0C", midLight: "#D69975", midDark: "#B9602D",
            deep: "#441F0A", onSolid: "#F9D8C4", onSolidBright: "#FFE9DC"),
        new("amber", "Accent_Amber",
            solid: "#97650E", ink: "#754A00", glow: "#FBB336",
            softLight: "#F7EAD4", softDark: "#5B3F0E", strongLight: "#FFDEA5", strongDark: "#724B07",
            washLight: "#F8EBD5", washDark: "#412D0B", midLight: "#CF9F4B", midDark: "#9D7022",
            deep: "#462F08", onSolid: "#F8DBA8", onSolidBright: "#FFF2DC"),
        new("graphite", "Accent_Graphite",
            solid: "#69707B", ink: "#4C545D", glow: "#BCC0C7",
            softLight: "#E4E5E7", softDark: "#313438", strongLight: "#CED1D6", strongDark: "#373C42",
            washLight: "#E5E6E8", washDark: "#232629", midLight: "#A3A8AD", midDark: "#727982",
            deep: "#24262A", onSolid: "#DCDFE2", onSolidBright: "#ECEDEF"),
    ];

    /// <summary>The presets, then the phone's own colour (Material You) where it has one.</summary>
    public static IReadOnlyList<AccentScheme> All { get; } = FromSystem() is { } system ? [.. Presets, system] : Presets;

    /// <summary>Until one is picked: the phone's own colour where it has one, green elsewhere.</summary>
    public static AccentScheme Default { get; } = All.Count > Presets.Length ? All[^1] : Presets[0];

    public const string SystemId = "system";

    public static AccentScheme Find(string? id) =>
        All.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Default;

    /// <summary>
    /// Material You: the tones of the wallpaper's primary colour, which Android 12 and later publish as
    /// system_accent1_*. Each role takes the tone Material uses for it; Solid and the two text tones are stepped
    /// until they hold the same contrast the presets are tuned to, whatever the wallpaper's colour.
    /// </summary>
    private static AccentScheme? FromSystem()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(31))
            return null;

        try
        {
            var context = Platform.AppContext;
            var tones = new Dictionary<int, int>();
            foreach (var tone in new[] { 50, 100, 200, 300, 400, 500, 600, 700, 800, 900 })
            {
                var id = context.Resources!.GetIdentifier($"system_accent1_{tone}", "color", "android");
                if (id == 0)
                    return null;

                tones[tone] = (int)(context.GetColor(id) & 0xFFFFFF);
            }

            string Hex(int tone) => "#" + tones[tone].ToString("X6");
            var solid = Pick(tones, 600, 1, 4.5, 0xFFFFFF);
            var ink = Pick(tones, 700, 1, 4.5, 0xFFFFFF);
            var glow = Pick(tones, 200, -1, 4.5, 0x1E1F22);
            var bubble = Pick(tones, 200, -1, MinBubbleBody, BubbleInk);
            return new AccentScheme(SystemId, "Accent_System",
                solid: Hex(solid), ink: Hex(ink), glow: Hex(glow),
                softLight: Hex(100), softDark: Hex(800), strongLight: Hex(200), strongDark: Hex(700),
                washLight: Hex(50), washDark: Hex(900), midLight: Hex(300), midDark: Hex(500),
                deep: Hex(900), onSolid: Hex(100), onSolidBright: Hex(50),
                bubbleLight: Hex(bubble), bubbleDark: Hex(bubble));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Reading the system accent failed: {ex}");
            return null;
        }
    }

    // The first tone from the start one that reaches the contrast: step +1 goes darker (a tone that carries white), -1 lighter
    // (a tone that sits on a dark surface).
    private static int Pick(Dictionary<int, int> tones, int start, int step, double minimum, int against)
    {
        var steps = new[] { 50, 100, 200, 300, 400, 500, 600, 700, 800, 900 };
        var index = Array.IndexOf(steps, start);
        for (var i = index; i >= 0 && i < steps.Length; i += step)
        {
            if (Contrast(tones[steps[i]], against) >= minimum)
                return steps[i];
        }

        return steps[step > 0 ? steps.Length - 1 : 0];
    }

    // The text on a sent message. The fill is light in both themes, so the text is the same in both.
    private const int BubbleInk = 0x1A1C1E;

    // Room above the 4.5 that body text needs, so the muted tone can still reach it.
    private const double MinBubbleBody = 7;

    private const double MinMuted = 4.5;

    // The colour nearest to `from` on the way to `to` that holds the contrast against the fill.
    private static int Toward(int from, int to, int fill, double minimum)
    {
        for (var step = 0; step <= 20; step++)
        {
            var color = Mix(from, to, step / 20.0);
            if (Contrast(color, fill) >= minimum)
                return color;
        }

        return to;
    }

    private static int Mix(int from, int to, double amount)
    {
        static int Channel(int a, int b, double t) => (int)Math.Round(a + (b - a) * t);
        return Channel(from >> 16 & 0xFF, to >> 16 & 0xFF, amount) << 16 |
               Channel(from >> 8 & 0xFF, to >> 8 & 0xFF, amount) << 8 |
               Channel(from & 0xFF, to & 0xFF, amount);
    }

    private static int Parse(string hex) => Convert.ToInt32(hex[1..], 16);

    private static string Format(int rgb) => "#" + rgb.ToString("X6");

    private static double Contrast(int a, int b)
    {
        var (hi, lo) = (Luminance(a), Luminance(b));
        if (hi < lo)
            (hi, lo) = (lo, hi);
        return (hi + 0.05) / (lo + 0.05);
    }

    private static double Luminance(int rgb)
    {
        static double Channel(int c)
        {
            var v = c / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(rgb >> 16 & 0xFF) + 0.7152 * Channel(rgb >> 8 & 0xFF) + 0.0722 * Channel(rgb & 0xFF);
    }

    private readonly string _solid, _ink, _glow, _deep, _onSolid, _onSolidBright;
    private readonly string _softLight, _softDark, _strongLight, _strongDark, _washLight, _washDark, _midLight, _midDark;
    private readonly string _bubbleLight, _bubbleDark, _mutedLight, _mutedDark, _linkLight, _linkDark;

    private AccentScheme(
        string id, string nameKey,
        string solid, string ink, string glow,
        string softLight, string softDark, string strongLight, string strongDark,
        string washLight, string washDark, string midLight, string midDark,
        string deep, string onSolid, string onSolidBright,
        string? bubbleLight = null, string? bubbleDark = null)
    {
        Id = id;
        NameKey = nameKey;
        _solid = solid;
        _ink = ink;
        _glow = glow;
        _softLight = softLight;
        _softDark = softDark;
        _strongLight = strongLight;
        _strongDark = strongDark;
        _washLight = washLight;
        _washDark = washDark;
        _midLight = midLight;
        _midDark = midDark;
        _deep = deep;
        _onSolid = onSolid;
        _onSolidBright = onSolidBright;

        // A sent message is the Strong tint in light and the Glow in dark unless a scheme chose its own tones. The time
        // and ticks are the text tone moved toward the fill until it only just holds 4.5, the link is the accent's
        // dark tone moved toward the text until it holds 4.5.
        _bubbleLight = bubbleLight ?? strongLight;
        _bubbleDark = bubbleDark ?? glow;
        var (light, dark) = (Parse(_bubbleLight), Parse(_bubbleDark));
        _mutedLight = Format(Toward(light, BubbleInk, light, MinMuted));
        _mutedDark = Format(Toward(dark, BubbleInk, dark, MinMuted));
        _linkLight = Format(Toward(Parse(ink), BubbleInk, light, 4.5));
        _linkDark = Format(Toward(Parse(ink), BubbleInk, dark, 4.5));
    }

    /// <summary>The value stored in the preferences.</summary>
    public string Id { get; }

    /// <summary>The resource key of the colour's name.</summary>
    public string NameKey { get; }

    /// <summary>The swatch shown in the picker.</summary>
    public string Swatch => _solid;

    public string Hex(AccentRole role, bool dark) => role switch
    {
        AccentRole.Solid => _solid,
        AccentRole.Text => dark ? _glow : _ink,
        AccentRole.Icon => dark ? _glow : _solid,
        AccentRole.Soft => dark ? _softDark : _softLight,
        AccentRole.Strong => dark ? _strongDark : _strongLight,
        AccentRole.Wash => dark ? _washDark : _washLight,
        AccentRole.Mid => dark ? _midDark : _midLight,
        AccentRole.Deep => _deep,
        AccentRole.Glow => _glow,
        AccentRole.OnSolid => _onSolid,
        AccentRole.OnSolidBright => _onSolidBright,
        AccentRole.Bubble => dark ? _bubbleDark : _bubbleLight,
        AccentRole.OnBubble => Format(BubbleInk),
        AccentRole.OnBubbleMuted => dark ? _mutedDark : _mutedLight,
        AccentRole.OnBubbleLink => dark ? _linkDark : _linkLight,
        _ => _solid
    };
}
