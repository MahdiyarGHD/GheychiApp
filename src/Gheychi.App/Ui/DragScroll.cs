using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace Gheychi.App.Ui;

/// <summary>
/// A row that is wider than its place and slides sideways under a finger, with a fling. It does its own dragging, not a
/// ScrollViewer's gesture: inside the pages' own swipes that gesture never got to start. A tap on what is inside still
/// reaches it; only a drag takes the pointer.
/// </summary>
public sealed class DragScroll : Decorator
{
    private const double Slop = 8;
    private const double Friction = 0.0042;

    private readonly TranslateTransform _move = new();
    private readonly DispatcherTimer _fling = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private bool _down;
    private bool _dragging;
    private double _startX;
    private double _startOffset;
    private double _lastX;
    private long _lastTick;
    private double _velocity;

    public DragScroll()
    {
        ClipToBounds = true;
        _fling.Tick += OnFling;
        AddHandler(PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, OnCaptureLost, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>How far the content is slid, in layout coordinates: 0 at its start, negative once it moved toward its end.</summary>
    private double Offset
    {
        get => _move.X;
        set => _move.X = Math.Clamp(value, -MaxSlide, 0);
    }

    private double MaxSlide => Child is null ? 0 : Math.Max(0, Child.DesiredSize.Width - Bounds.Width);

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child is null)
            return default;

        Child.RenderTransform = _move;
        Child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
        var width = double.IsInfinity(availableSize.Width) ? Child.DesiredSize.Width : Math.Min(Child.DesiredSize.Width, availableSize.Width);
        return new Size(width, Child.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child is not null)
            Child.Arrange(new Rect(0, 0, Math.Max(finalSize.Width, Child.DesiredSize.Width), finalSize.Height));

        _move.X = Math.Clamp(_move.X, -Math.Max(0, (Child?.DesiredSize.Width ?? 0) - finalSize.Width), 0);
        return finalSize;
    }

    /// <summary>Slides the row, if it needs it, so that <paramref name="item"/> is fully in view.</summary>
    public void Reveal(Control item)
    {
        if (Child is null || item.TranslatePoint(default, Child) is not { } at)
            return;

        const double margin = 16;
        var left = at.X + Offset;
        var right = left + item.Bounds.Width;
        if (left < margin)
            Offset -= left - margin;
        else if (right > Bounds.Width - margin)
            Offset -= right - (Bounds.Width - margin);
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        _fling.Stop();
        if (MaxSlide <= 0)
            return;

        _down = true;
        _dragging = false;
        _velocity = 0;
        _startX = _lastX = e.GetPosition(this).X;
        _startOffset = Offset;
        _lastTick = _clock.ElapsedMilliseconds;
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (!_down)
            return;

        var x = e.GetPosition(this).X;
        if (!_dragging)
        {
            if (Math.Abs(x - _startX) < Slop)
                return;

            _dragging = true;
            e.Pointer.Capture(this);
        }

        Offset = _startOffset + (x - _startX);

        var now = _clock.ElapsedMilliseconds;
        var dt = Math.Max(1, now - _lastTick);
        _velocity = 0.7 * _velocity + 0.3 * ((x - _lastX) / dt);
        _lastX = x;
        _lastTick = now;
        e.Handled = true;
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        var wasDragging = _dragging;
        _down = false;
        _dragging = false;
        if (!wasDragging)
            return;

        e.Pointer.Capture(null);
        e.Handled = true;
        if (_clock.ElapsedMilliseconds - _lastTick < 90 && Math.Abs(_velocity) > 0.25)
        {
            _lastTick = _clock.ElapsedMilliseconds;
            _fling.Start();
        }
    }

    private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _down = false;
        _dragging = false;
    }

    private void OnFling(object? sender, EventArgs e)
    {
        var now = _clock.ElapsedMilliseconds;
        var dt = Math.Min(48, now - _lastTick);
        _lastTick = now;

        var before = Offset;
        Offset = before + _velocity * dt;
        _velocity *= Math.Exp(-Friction * dt);
        if (Math.Abs(_velocity) < 0.03 || Offset == before)
            _fling.Stop();
    }
}
