using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Gheychi.App.Controls.Settings;

/// <summary>Bars with rounded tops, bottom-aligned, today (the last value) in the strong colour. One visual, drawn in one pass.</summary>
public sealed class BarChart : Control
{
    private const double MaxBarHeight = 86;
    private const double Gap = 4;
    private const double Radius = 4;

    private static readonly IBrush EmptyBrush = Palette.Pick("#D5D7D9", "#323335");
    private static readonly IBrush TodayBrush = Palette.Pick("#1B5E43", "#6FD3A8");
    private static readonly IBrush DayBrush = Palette.Pick("#6FB592", "#3F8566");

    private IReadOnlyList<int> _values = [];

    public BarChart()
    {
        IsHitTestVisible = false;
    }

    public IReadOnlyList<int> Values
    {
        get => _values;
        set
        {
            _values = value;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        var values = _values;
        if (values.Count == 0)
            return;

        var width = Bounds.Width;
        var height = Bounds.Height;
        var barWidth = (width - Gap * (values.Count - 1)) / values.Count;
        if (barWidth <= 0)
            return;

        var max = Math.Max(1, values.Max());
        var top = new Vector(Radius, Radius);
        var flat = new Vector(0, 0);
        for (var i = 0; i < values.Count; i++)
        {
            var barHeight = values[i] == 0 ? 2 : Math.Max(5, values[i] * MaxBarHeight / max);
            barHeight = Math.Min(barHeight, height);
            var brush = values[i] == 0 ? EmptyBrush : i == values.Count - 1 ? TodayBrush : DayBrush;
            var rect = new Rect(i * (barWidth + Gap), height - barHeight, barWidth, barHeight);
            context.DrawRectangle(brush, null, new RoundedRect(rect, top, top, flat, flat));
        }
    }
}
