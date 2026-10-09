using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Gheychi.App.Ui;

public static class ScrollAnimator
{
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(260);
    private static DispatcherTimer? _running;

    /// <summary>
    /// Scrolls back to the top. A far-off list first jumps to within a few screens of it, so the rows it would
    /// fly past are never built.
    /// </summary>
    public static void ToTop(ScrollViewer? scroll)
    {
        _running?.Stop();
        if (scroll is null || scroll.Offset.Y <= 0)
            return;

        var from = Math.Min(scroll.Offset.Y, Math.Max(scroll.Viewport.Height, 1) * 3);
        var last = from;
        scroll.Offset = new Vector(scroll.Offset.X, from);

        var clock = Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            // The finger took over.
            if (Math.Abs(scroll.Offset.Y - last) > 1)
            {
                timer.Stop();
                return;
            }

            var t = Math.Min(1, clock.Elapsed / Duration);
            last = from * Math.Pow(1 - t, 3);
            scroll.Offset = new Vector(scroll.Offset.X, last);
            if (t >= 1)
                timer.Stop();
        };
        _running = timer;
        timer.Start();
    }
}
