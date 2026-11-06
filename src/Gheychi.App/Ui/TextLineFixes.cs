using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace Gheychi.App.Ui;

/// <summary>
/// Corrects every line Avalonia 12.1.3 formats for two bugs in right-to-left and mixed-direction text.
/// <para>
/// Run order: for each run in reading order a line keeps the place the run is drawn at, but Avalonia stores the reverse
/// lookup (which run is drawn at that place). The two only differ when a line holds two runs in a row against the
/// line's direction, such as a Latin word and a number in Persian text, or a word drawn in a fallback font. The caret,
/// the selection and the touch handles are then measured on the wrong run: the handles jump about while dragged, and
/// measuring some selections throws ("Covered length must be greater than zero"), which crashed the app.
/// </para>
/// <para>
/// Right edge: a wrapped line keeps the space it broke at, and in right-to-left text that space is drawn on the line's
/// left. Avalonia lines it up against the right edge with that space counted, and when the space does not fit it starts
/// the line at the left edge instead of before it, so the words are pushed past the right edge by up to a space's width
/// and the first letter of the line is cut by the message bubble.
/// </para>
/// </summary>
internal sealed class TextLineFixes(TextFormatter inner) : TextFormatter
{
    private static FieldInfo? _indexedRuns;
    private static PropertyInfo? _textRun;
    private static PropertyInfo? _runIndex;
    private static FieldInfo? _metrics;
    private static FieldInfo? _bounds;
    private static FieldInfo? _inkBounds;

    // Kept for the trimmed release build, which only sees these members used by name.
    [DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicFields, "Avalonia.Media.TextFormatting.TextLineImpl", "Avalonia.Base")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, "Avalonia.Media.TextFormatting.IndexedTextRun", "Avalonia.Base")]
    public static AppBuilder Register(AppBuilder builder) =>
        builder.With<TextFormatter>(new TextLineFixes(TextFormatter.Current));

    public override TextLine? FormatLine(ITextSource textSource, int firstTextSourceIndex, double paragraphWidth,
        TextParagraphProperties paragraphProperties, TextLineBreak? previousLineBreak = null) =>
        Fix(inner.FormatLine(textSource, firstTextSourceIndex, paragraphWidth, paragraphProperties, previousLineBreak),
            paragraphWidth, paragraphProperties);

    public override TextLine? FormatLine(ITextSource textSource, int firstTextSourceIndex, double paragraphWidth,
        TextParagraphProperties paragraphProperties, TextLineBreak? previousLineBreak, TextRunCache? textRunCache) =>
        Fix(inner.FormatLine(textSource, firstTextSourceIndex, paragraphWidth, paragraphProperties, previousLineBreak, textRunCache),
            paragraphWidth, paragraphProperties);

    private static TextLine? Fix(TextLine? line, double paragraphWidth, TextParagraphProperties properties)
    {
        if (line is null)
            return null;

        FixRunOrder(line);
        FixRightEdge(line, paragraphWidth, properties);
        return line;
    }

    private static void FixRunOrder(TextLine line)
    {
        if (line.TextRuns.Count < 3)
            return;

        _indexedRuns ??= line.GetType().GetField("_indexedTextRuns", BindingFlags.Instance | BindingFlags.NonPublic);
        if (_indexedRuns?.GetValue(line) is not IList indexed)
            return;

        // TextRuns is in drawing order, so a run's place is its position there.
        var drawn = line.TextRuns;
        foreach (var item in indexed)
        {
            _textRun ??= item.GetType().GetProperty("TextRun");
            _runIndex ??= item.GetType().GetProperty("RunIndex");
            if (_textRun is null || _runIndex is null)
                return;

            var run = _textRun.GetValue(item);
            for (var place = 0; place < drawn.Count; place++)
            {
                if (ReferenceEquals(drawn[place], run))
                {
                    _runIndex.SetValue(item, place);
                    break;
                }
            }
        }
    }

    private static void FixRightEdge(TextLine line, double paragraphWidth, TextParagraphProperties properties)
    {
        var overflow = line.WidthIncludingTrailingWhitespace - paragraphWidth;
        // Only the trailing space overflowing: a word too long for the line is left as Avalonia places it.
        if (overflow <= 0 || line.Width > paragraphWidth + 0.01 || double.IsInfinity(paragraphWidth) ||
            properties.FlowDirection != FlowDirection.RightToLeft ||
            properties.TextAlignment is not (TextAlignment.Start or TextAlignment.Justify or TextAlignment.Right))
            return;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = line.GetType();
        _metrics ??= type.GetField("_textLineMetrics", flags);
        _bounds ??= type.GetField("_bounds", flags);
        _inkBounds ??= type.GetField("_inkBounds", flags);
        if (_metrics?.GetValue(line) is not TextLineMetrics metrics || metrics.Start != 0)
            return;

        _metrics.SetValue(line, metrics with { Start = -overflow });
        var shift = new Vector(-overflow, 0);
        if (_bounds?.GetValue(line) is Rect bounds)
            _bounds.SetValue(line, bounds.Translate(shift));
        if (_inkBounds?.GetValue(line) is Rect ink)
            _inkBounds.SetValue(line, ink.Translate(shift));
    }
}
