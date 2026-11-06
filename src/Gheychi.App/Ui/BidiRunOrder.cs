using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Avalonia;
using Avalonia.Media.TextFormatting;

namespace Gheychi.App.Ui;

/// <summary>
/// Corrects every line Avalonia formats for an Avalonia 12.1.3 bug in mixed-direction text. For each run in reading
/// order a line keeps the place the run is drawn at, but Avalonia stores the reverse lookup (which run is drawn at that
/// place). The two only differ when a line holds two runs in a row against the line's direction, such as a Latin word
/// and a number in Persian text, or a word drawn in a fallback font. The caret, the selection and the touch handles are
/// then measured on the wrong run: the handles jump about while dragged, and measuring some selections throws
/// ("Covered length must be greater than zero"), which crashed the app.
/// </summary>
internal sealed class BidiRunOrder(TextFormatter inner) : TextFormatter
{
    private static FieldInfo? _indexedRuns;
    private static PropertyInfo? _textRun;
    private static PropertyInfo? _runIndex;

    // Kept for the trimmed release build, which only sees these members used by name.
    [DynamicDependency("_indexedTextRuns", "Avalonia.Media.TextFormatting.TextLineImpl", "Avalonia.Base")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, "Avalonia.Media.TextFormatting.IndexedTextRun", "Avalonia.Base")]
    public static AppBuilder Register(AppBuilder builder) =>
        builder.With<TextFormatter>(new BidiRunOrder(TextFormatter.Current));

    public override TextLine? FormatLine(ITextSource textSource, int firstTextSourceIndex, double paragraphWidth,
        TextParagraphProperties paragraphProperties, TextLineBreak? previousLineBreak = null) =>
        Fix(inner.FormatLine(textSource, firstTextSourceIndex, paragraphWidth, paragraphProperties, previousLineBreak));

    public override TextLine? FormatLine(ITextSource textSource, int firstTextSourceIndex, double paragraphWidth,
        TextParagraphProperties paragraphProperties, TextLineBreak? previousLineBreak, TextRunCache? textRunCache) =>
        Fix(inner.FormatLine(textSource, firstTextSourceIndex, paragraphWidth, paragraphProperties, previousLineBreak, textRunCache));

    private static TextLine? Fix(TextLine? line)
    {
        if (line is null || line.TextRuns.Count < 3)
            return line;

        _indexedRuns ??= line.GetType().GetField("_indexedTextRuns", BindingFlags.Instance | BindingFlags.NonPublic);
        if (_indexedRuns?.GetValue(line) is not IList indexed)
            return line;

        // TextRuns is in drawing order, so a run's place is its position there.
        var drawn = line.TextRuns;
        foreach (var item in indexed)
        {
            _textRun ??= item.GetType().GetProperty("TextRun");
            _runIndex ??= item.GetType().GetProperty("RunIndex");
            if (_textRun is null || _runIndex is null)
                return line;

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

        return line;
    }
}
