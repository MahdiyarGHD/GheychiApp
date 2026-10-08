using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Gheychi.App.Ui;

/// <summary>A 24x24 viewBox vector icon filled with <see cref="Foreground"/>, scaled to fit the control.</summary>
public sealed class Icon : Control
{
    private const double ViewBox = 24;

    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<Icon, Geometry?>(nameof(Data));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<Icon, IBrush?>(nameof(Foreground));

    static Icon()
    {
        AffectsRender<Icon>(DataProperty, ForegroundProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<Icon>(false);
    }

    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(
            double.IsNaN(Width) ? Math.Min(ViewBox, availableSize.Width) : Width,
            double.IsNaN(Height) ? Math.Min(ViewBox, availableSize.Height) : Height);

    public override void Render(DrawingContext context)
    {
        var data = Data;
        var brush = Foreground;
        if (data is null || brush is null)
            return;

        var bounds = Bounds;
        var scale = Math.Min(bounds.Width, bounds.Height) / ViewBox;
        if (scale <= 0)
            return;

        var offsetX = (bounds.Width - ViewBox * scale) / 2;
        var offsetY = (bounds.Height - ViewBox * scale) / 2;
        using (context.PushTransform(new Matrix(scale, 0, 0, scale, offsetX, offsetY)))
            context.DrawGeometry(brush, null, data);
    }
}
