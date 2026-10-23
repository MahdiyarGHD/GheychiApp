using Avalonia.Platform;
using Gheychi.Core.Emoji;

namespace Gheychi.App.Ui.Emoji;

/// <summary>The emoji the phone can draw, by category (Recent excluded), and the set of them for checking a stored one.</summary>
internal sealed class EmojiSet(string[][] categories)
{
    private HashSet<string>? _all;

    /// <summary>One list per category after Recent, in the order of <see cref="EmojiCategory"/>.</summary>
    public string[][] Categories { get; } = categories;

    public bool Contains(string emoji)
    {
        if (_all is null)
        {
            var all = new HashSet<string>(StringComparer.Ordinal);
            foreach (var category in Categories)
                all.UnionWith(category);
            _all = all;
        }

        return _all.Contains(emoji);
    }
}

/// <summary>
/// Reads the emoji file and drops what the phone cannot draw, once per process and off the UI thread. Nothing runs
/// until the picker is first shown.
/// </summary>
internal static class EmojiData
{
    private static readonly Uri Source = new("avares://Gheychi.App/UiAssets/Emoji/emoji.txt");

    private static Task<EmojiSet>? _loading;

    /// <summary>A load that failed is not kept: the next call tries again.</summary>
    public static Task<EmojiSet> LoadAsync()
    {
        var loading = _loading ??= Task.Run(Load);
        if (loading.IsFaulted)
            _loading = loading = Task.Run(Load);
        return loading;
    }

    private static EmojiSet Load()
    {
        string text;
        using (var stream = AssetLoader.Open(Source))
        using (var reader = new StreamReader(stream))
            text = reader.ReadToEnd();

        var categories = EmojiCatalog.Parse(text);
        if (!KeepsEverything())
        {
            // A Paint is not thread safe: this one never leaves the task.
            using var paint = new global::Android.Graphics.Paint();
            for (var i = 0; i < categories.Length; i++)
                categories[i] = categories[i].Where(paint.HasGlyph).ToArray();
        }

        return new EmojiSet(categories);
    }

    /// <summary>
    /// With an updated emoji font (Play system update) the text engine draws through that font first, and it has
    /// every emoji the file lists: Paint.HasGlyph only asks the system partition's copy, which would hide emoji that
    /// do draw. Without one the system font is the only one, and HasGlyph is exactly "this draws as a single
    /// picture", so an emoji it refuses (it would show as boxes or as its parts) is not offered.
    /// </summary>
    private static bool KeepsEverything() => Platforms.Android.SystemEmoji.Fallbacks is not null;
}
