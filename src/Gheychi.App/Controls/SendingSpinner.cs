using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Gheychi.App.Controls;

/// <summary>
/// The 12x12 busy ring in a message that is being sent. It spins through a compositor animation, so nothing runs
/// on the UI thread per frame, and only while it is visible.
/// </summary>
internal sealed class SendingSpinner : Control
{
    private const double Size = 12;
    private const double Thickness = 1.5;

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<SendingSpinner, IBrush?>(nameof(Foreground));

    private bool _spinning;
    private bool _waitingForVisual;

    static SendingSpinner()
    {
        AffectsRender<SendingSpinner>(ForegroundProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<SendingSpinner>(false);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    public override void Render(DrawingContext context)
    {
        if (Foreground is not { } brush)
            return;

        const double radius = (Size - Thickness) / 2;
        const double center = Size / 2;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(center, center - radius), false);
            ctx.ArcTo(new Point(center - radius, center), new Size(radius, radius), 0, false, SweepDirection.CounterClockwise);
            ctx.ArcTo(new Point(center, center + radius), new Size(radius, radius), 0, false, SweepDirection.CounterClockwise);
            ctx.ArcTo(new Point(center + radius, center), new Size(radius, radius), 0, false, SweepDirection.CounterClockwise);
            ctx.EndFigure(false);
        }

        context.DrawGeometry(null, new Pen(brush, Thickness, lineCap: PenLineCap.Round), geometry);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Sync();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Stop();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty)
            Sync();
    }

    private void Sync()
    {
        if (IsVisible && this.IsAttachedToVisualTree())
            Start();
        else
            Stop();
    }

    private void Start()
    {
        if (_spinning)
            return;

        if (ElementComposition.GetElementVisual(this) is not { } visual)
        {
            // The compositor side of a control that was just attached exists a moment later.
            if (!_waitingForVisual)
            {
                _waitingForVisual = true;
                Dispatcher.UIThread.Post(() =>
                {
                    _waitingForVisual = false;
                    Sync();
                }, DispatcherPriority.Loaded);
            }

            return;
        }

        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.Target = "RotationAngle";
        animation.InsertKeyFrame(0f, 0f);
        animation.InsertKeyFrame(1f, MathF.Tau, new LinearEasing());
        animation.Duration = TimeSpan.FromMilliseconds(900);
        animation.IterationBehavior = AnimationIterationBehavior.Forever;

        visual.CenterPoint = new Vector3D(Size / 2, Size / 2, 0);
        visual.StartAnimation("RotationAngle", animation);
        _spinning = true;
    }

    private void Stop()
    {
        if (!_spinning)
            return;

        _spinning = false;
        if (ElementComposition.GetElementVisual(this) is { } visual)
            visual.StopAnimation("RotationAngle");
    }
}
