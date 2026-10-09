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
    OnSolidBright
}

/// <summary>
/// One accent colour of the app, as a hand-tuned set of tones. Every scheme keeps the same contrast against white
/// (Solid), against the light and dark surfaces (Text) and against its own fill (OnSolid), so switching the accent
/// never costs legibility. Green is the original look.
/// </summary>
public sealed class AccentScheme
{
    public static IReadOnlyList<AccentScheme> All { get; } =
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

    public static AccentScheme Default => All[0];

    public static AccentScheme Find(string? id) =>
        All.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Default;

    private readonly string _solid, _ink, _glow, _deep, _onSolid, _onSolidBright;
    private readonly string _softLight, _softDark, _strongLight, _strongDark, _washLight, _washDark, _midLight, _midDark;

    private AccentScheme(
        string id, string nameKey,
        string solid, string ink, string glow,
        string softLight, string softDark, string strongLight, string strongDark,
        string washLight, string washDark, string midLight, string midDark,
        string deep, string onSolid, string onSolidBright)
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
        _ => _solid
    };
}
