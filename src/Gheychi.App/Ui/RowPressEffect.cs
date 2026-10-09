using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Gheychi.App.Theming;

namespace Gheychi.App.Ui;

/// <summary>
/// The pressed highlight of a list row, like Android's. One translucent strip is moved onto the row under the finger
/// instead of every row carrying a pressed state of its own, so a list costs nothing until it is touched.
/// </summary>
public sealed class RowPressEffect
{
    private static readonly TimeSpan ShowDelay = TimeSpan.FromMilliseconds(60);
    private static readonly TimeSpan MinimumShown = TimeSpan.FromMilliseconds(110);
    private static readonly TimeSpan LongestShown = TimeSpan.FromMilliseconds(1500);
    private const double Slop = 10;

    // A touch that lands on a list still flinging is meant to stop it, not to press a row.
    private const long FlingGraceMs = 150;

    private readonly Control _list;
    private readonly Panel _host;
    private readonly Border _glow;
    private readonly TranslateTransform _move = new();
    private readonly DispatcherTimer _timer = new();
    private readonly DispatcherTimer _safety = new();
    private ScrollViewer? _scroll;
    private long _lastScrolled;
    private Visual? _row;
    private Point _start;
    private bool _shown;
    private bool _released;

    private RowPressEffect(Control list, Panel host)
    {
        _list = list;
        _host = host;
        _glow = new Border
        {
            IsHitTestVisible = false,
            Opacity = 0,
            IsVisible = false,
            VerticalAlignment = VerticalAlignment.Top,
            Background = Palette.Pick("#14000000", "#1AFFFFFF"),
            RenderTransform = _move,
            Transitions = [new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(140) }]
        };
        host.Children.Add(_glow);
        _timer.Tick += OnTimer;
        _safety.Interval = LongestShown;
        _safety.Tick += (_, _) => Reset();

        list.AddHandler(InputElement.PointerPressedEvent, OnPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        list.AddHandler(InputElement.PointerMovedEvent, OnMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        list.AddHandler(InputElement.PointerReleasedEvent, OnReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        list.DetachedFromVisualTree += (_, _) => Reset();
    }

    /// <param name="list">The list whose rows are highlighted; its rows are the children of its virtualizing panel.</param>
    /// <param name="host">The panel the list is in; the highlight is added above it.</param>
    public static void Attach(Control list, Panel host) => _ = new RowPressEffect(list, host);

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        Reset();
        WatchScroll();
        if (Environment.TickCount64 - _lastScrolled < FlingGraceMs ||
            !e.GetCurrentPoint(_host).Properties.IsLeftButtonPressed || RowOf(e.Source as Visual) is not { } row)
            return;

        _row = row;
        _released = false;
        _start = e.GetPosition(_host);
        _timer.Interval = ShowDelay;
        _timer.Start();
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_row is null || _released)
            return;

        var now = e.GetPosition(_host);
        if (Math.Abs(now.X - _start.X) > Slop || Math.Abs(now.Y - _start.Y) > Slop)
            Fade();
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_row is null)
            return;

        _released = true;
        if (!_shown)
            Show();

        // A quick tap still shows the highlight for a moment.
        _timer.Stop();
        _timer.Interval = MinimumShown;
        _timer.Start();
    }

    private void OnTimer(object? sender, EventArgs e)
    {
        _timer.Stop();
        if (_released)
            Fade();
        else
            Show();
    }

    // The highlight is placed once; a list that scrolls under it would leave it on whatever row passes.
    private void WatchScroll()
    {
        if (_scroll is not null)
            return;

        _scroll = _list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (_scroll is not null)
            _scroll.ScrollChanged += OnScrolled;
    }

    private void OnScrolled(object? sender, ScrollChangedEventArgs e)
    {
        if (e.OffsetDelta == default)
            return;

        _lastScrolled = Environment.TickCount64;
        if (_row is not null || _glow.IsVisible)
            Reset();
    }

    private void Show()
    {
        if (_row is not { } row || row.TranslatePoint(default, _host) is not { } at)
            return;

        _shown = true;
        _move.Y = at.Y;
        _glow.Height = row.Bounds.Height;
        _glow.IsVisible = true;
        _glow.Opacity = 1;
        _safety.Stop();
        _safety.Start();
    }

    private void Fade()
    {
        _timer.Stop();
        _row = null;
        _shown = false;
        _glow.Opacity = 0;
    }

    private void Reset()
    {
        _timer.Stop();
        _safety.Stop();
        _row = null;
        _shown = false;
        _glow.Opacity = 0;
        _glow.IsVisible = false;
    }

    private static Visual? RowOf(Visual? source)
    {
        for (var visual = source; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual.GetVisualParent() is VirtualizingStackPanel)
                return visual;
        }

        return null;
    }
}
