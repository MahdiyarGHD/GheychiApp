using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Gheychi.App.Theming;

namespace Gheychi.App.Controls;

/// <summary>A row of the emoji list: a category title, or a run of emoji. Every row is as tall as the others.</summary>
internal sealed record EmojiRowItem(string? Title, string[]? Cells);

/// <summary>
/// Shaped text of the emoji list, shared by its rows. A row that scrolls back into view finds its emoji here
/// instead of having them shaped again.
/// </summary>
internal sealed class EmojiShaper
{
    private readonly Dictionary<string, FormattedText> _glyphs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FormattedText> _titles = new(StringComparer.Ordinal);

    public FormattedText Glyph(string emoji)
    {
        if (!_glyphs.TryGetValue(emoji, out var text))
            _glyphs[emoji] = text = new FormattedText(emoji, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                Typeface.Default, EmojiRowView.GlyphSize, Brushes.Black);
        return text;
    }

    public FormattedText Title(string title, bool rtl)
    {
        var key = (rtl ? "r" : "l") + title;
        if (!_titles.TryGetValue(key, out var text))
            _titles[key] = text = new FormattedText(title, CultureInfo.CurrentUICulture,
                rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, new Typeface(AppFonts.Bold), 12,
                Palette.Pick("#5F6368", "#9AA0A6"));
        return text;
    }
}

/// <summary>
/// One row of the emoji list drawn as a whole: no control per emoji, no binding, one pass of drawing when a row is
/// given new content. It is always left to right and places its columns itself, mirrored when the page is right to
/// left, so the emoji themselves are never flipped. A tap is resolved to its emoji with <see cref="CellAt"/>.
/// </summary>
internal sealed class EmojiRowView : Control
{
    public const double RowHeight = 44;
    public const double GlyphSize = 28;
    private const double TitleInset = 16;

    private readonly EmojiShaper _shaper;
    private EmojiRowItem? _item;
    private int _columns = 1;
    private bool _rtl;

    public EmojiRowView(EmojiShaper shaper)
    {
        _shaper = shaper;
        FlowDirection = FlowDirection.LeftToRight;
    }

    public void Bind(EmojiRowItem item, int columns, bool rtl)
    {
        if (ReferenceEquals(_item, item) && _columns == columns && _rtl == rtl)
            return;

        _item = item;
        _columns = Math.Max(columns, 1);
        _rtl = rtl;
        InvalidateVisual();
    }

    /// <summary>The emoji under a point of the row, or null for a title or the gap past the last one.</summary>
    public string? CellAt(double x)
    {
        if (_item?.Cells is not { } cells || Bounds.Width <= 0)
            return null;

        var column = (int)(x / (Bounds.Width / _columns));
        if (_rtl)
            column = _columns - 1 - column;
        return column >= 0 && column < cells.Length ? cells[column] : null;
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, RowHeight);

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        // Transparent but drawn, so a tap between two emoji still lands on the row.
        context.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, RowHeight));

        if (_item is null)
            return;

        if (_item.Title is { } title)
        {
            var text = _shaper.Title(title, _rtl);
            var x = _rtl ? width - TitleInset - text.Width : TitleInset;
            context.DrawText(text, new Point(x, RowHeight - text.Height - 6));
            return;
        }

        if (_item.Cells is not { } cells)
            return;

        var cell = width / _columns;
        for (var i = 0; i < cells.Length; i++)
        {
            var glyph = _shaper.Glyph(cells[i]);
            var column = _rtl ? _columns - 1 - i : i;
            context.DrawText(glyph, new Point(column * cell + (cell - glyph.Width) / 2, (RowHeight - glyph.Height) / 2));
        }
    }
}
