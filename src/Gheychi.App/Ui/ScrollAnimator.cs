using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Gheychi.App.Ui;

public static class ScrollAnimator
{
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(260);
    private static Action? _stop;

    /// <summary>
    /// Scrolls back to the top, also while a fling is still running (it is ended, or it would carry the list on
    /// past the top). A far-off list first jumps to within a few screens of it, so the rows it would fly past are
    /// never built. A finger on the list stops it.
    /// </summary>
    public static void ToTop(ScrollViewer? scroll)
    {
        _stop?.Invoke();
        if (scroll is null || scroll.Offset.Y <= 0)
            return;

        var from = Math.Min(scroll.Offset.Y, Math.Max(scroll.Viewport.Height, 1) * 3);
        scroll.Offset = new Vector(scroll.Offset.X, from);

        var clock = Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };

        void Stop()
        {
            timer.Stop();
            scroll.RemoveHandler(InputElement.ScrollGestureEvent, EndFling);
            scroll.RemoveHandler(InputElement.PointerPressedEvent, OnFinger);
            _stop = null;
        }

        void EndFling(object? sender, ScrollGestureEventArgs e)
        {
            e.Handled = true;
            e.ShouldEndScrollGesture = true;
        }

        void OnFinger(object? sender, PointerPressedEventArgs e) => Stop();

        // The scroll viewer takes each step of the fling first; this ends it after that.
        scroll.AddHandler(InputElement.ScrollGestureEvent, EndFling, RoutingStrategies.Bubble, handledEventsToo: true);
        scroll.AddHandler(InputElement.PointerPressedEvent, OnFinger, RoutingStrategies.Tunnel, handledEventsToo: true);

        timer.Tick += (_, _) =>
        {
            var t = Math.Min(1, clock.Elapsed / Duration);
            scroll.Offset = new Vector(scroll.Offset.X, from * Math.Pow(1 - t, 3));
            if (t >= 1)
                Stop();
        };
        _stop = Stop;
        timer.Start();
    }
}
