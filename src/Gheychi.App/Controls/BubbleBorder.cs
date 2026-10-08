using Avalonia;
using Avalonia.Controls;

namespace Gheychi.App.Controls;

/// <summary>
/// A border whose thickness is a single number. The search outline of a message bubble changes between 0, 1.5 and 3
/// through a binding, and a binding cannot turn a number into a <see cref="Thickness"/>.
/// </summary>
internal sealed class BubbleBorder : Border
{
    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<BubbleBorder, double>(nameof(StrokeThickness));

    static BubbleBorder()
    {
        StrokeThicknessProperty.Changed.AddClassHandler<BubbleBorder>((border, e) =>
            border.BorderThickness = new Thickness(e.GetNewValue<double>()));
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(Border);
}
