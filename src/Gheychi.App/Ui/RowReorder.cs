using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Gheychi.App.Controls;
using Gheychi.App.Theming;
using Gheychi.Core.Services;

namespace Gheychi.App.Ui;

/// <summary>
/// Drag and drop of the first rows of a list, which all have one height. The held row follows the finger and the rows it
/// passes step aside, all as translations on the render thread: nothing is measured or laid out while the finger moves,
/// and the list's own order changes once, when the row is dropped.
/// </summary>
public sealed class RowReorder
{
    private const double StartDistance = 10;
    private static readonly TimeSpan ShiftTime = TimeSpan.FromMilliseconds(140);
    private static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(140);
    private static readonly IEasing Ease = new CubicEaseOut();

    private readonly ItemsControl _list;
    private readonly double _rowHeight;
    private readonly Func<int> _movableCount;
    private readonly Action _began;
    private readonly Action<int, int> _dropped;
    private readonly Action _ended;

    private readonly Dictionary<int, Shift> _shifts = [];
    private Point _press;
    private int _armed = -1;
    private bool _dragging;
    private bool _settling;
    private int _from;
    private int _to;
    private int _count;
    private double _offset;
    private Control? _dragged;

    private sealed record Shift(Control Row, double From, double To, long Start)
    {
        public double At(long now)
        {
            var t = Math.Clamp((now - Start) / ShiftTime.TotalMilliseconds, 0, 1);
            return From + (To - From) * Ease.Ease(t);
        }
    }

    /// <param name="list">The list; its rows have <paramref name="rowHeight"/> and are the children of a virtualizing panel.</param>
    /// <param name="movableCount">How many rows from the top can be moved.</param>
    /// <param name="began">A drag started: whatever rebuilds the list should wait.</param>
    /// <param name="dropped">The row at the first index was dropped at the second.</param>
    /// <param name="ended">The drag is over and the rows are back in place.</param>
    public RowReorder(ItemsControl list, double rowHeight, Func<int> movableCount, Action began, Action<int, int> dropped, Action ended)
    {
        _list = list;
        _rowHeight = rowHeight;
        _movableCount = movableCount;
        _began = began;
        _dropped = dropped;
        _ended = ended;
    }

    /// <summary>True from the first move of a drag until the rows are back in place.</summary>
    public bool IsBusy => _dragging || _settling;

    /// <summary>The row at this index is held: it is dragged if the finger moves on.</summary>
    public void Arm(int index)
    {
        if (!IsBusy && index >= 0 && index < _movableCount())
            _armed = index;
    }

    public void OnPressed(PointerPressedEventArgs e)
    {
        _press = e.GetPosition(_list);
        if (!IsBusy)
            _armed = -1;
    }

    public void OnMoved(PointerEventArgs e)
    {
        if (_armed < 0 && !_dragging)
            return;

        var moved = e.GetPosition(_list).Y - _press.Y;
        if (!_dragging)
        {
            if (Math.Abs(moved) < StartDistance)
                return;

            if (!Begin(e))
            {
                _armed = -1;
                return;
            }
        }

        // The list under the finger must not scroll with it, and nothing else may take the touch.
        e.PreventGestureRecognition();
        e.Handled = true;
        Follow(moved);
    }

    /// <returns>True when a drag was in progress and has been dropped.</returns>
    public bool OnReleased()
    {
        _armed = -1;
        if (!_dragging)
            return false;

        End(commit: true);
        return true;
    }

    public void OnCaptureLost(PointerCaptureLostEventArgs e)
    {
        if (_dragging && !ReferenceEquals(e.Pointer.Captured, _list))
            End(commit: false);
    }

    /// <summary>Puts a drag back where it began, e.g. when the page is left.</summary>
    public void Cancel()
    {
        _armed = -1;
        if (_dragging)
            End(commit: false);
    }

    private bool Begin(PointerEventArgs e)
    {
        _count = _movableCount();
        if (_armed >= _count || _list.ContainerFromIndex(_armed) is not { } row)
            return false;

        _from = _to = _armed;
        _offset = 0;
        _dragged = row;
        _shifts.Clear();

        e.Pointer.Capture(_list);
        _began();
        _dragging = true;

        Lift(row);
        ChatView.TriggerHaptic(global::Android.Views.FeedbackConstants.LongPress);
        return true;
    }

    private void Follow(double moved)
    {
        if (_dragged is null)
            return;

        // The row stays among the rows that can be moved.
        _offset = Math.Clamp(moved, -_from * _rowHeight, (_count - 1 - _from) * _rowHeight);
        OverlayAnimator.SetTranslation(_dragged, 0, _offset);

        var target = PinnedOrder.TargetIndex(_from, _offset, _rowHeight, _count);
        if (target == _to)
            return;

        _to = target;
        MakeRoom();
        ChatView.TriggerHaptic(global::Android.Views.FeedbackConstants.ClockTick);
    }

    // The rows between the start and the slot the row would fall into each step one row away from it.
    private void MakeRoom()
    {
        for (var index = 0; index < _count; index++)
        {
            if (index == _from)
                continue;

            var shift = 0.0;
            if (_to > _from && index > _from && index <= _to)
                shift = -_rowHeight;
            else if (_to < _from && index >= _to && index < _from)
                shift = _rowHeight;

            Slide(index, shift);
        }
    }

    private void Slide(int index, double to)
    {
        var known = _shifts.GetValueOrDefault(index);
        if (known is null)
        {
            if (to == 0 || _list.ContainerFromIndex(index) is not { } row)
                return;

            known = new Shift(row, 0, 0, 0);
        }
        else if (known.To == to)
        {
            return;
        }

        var now = Environment.TickCount64;
        var shift = new Shift(known.Row, known.At(now), to, now);
        _shifts[index] = shift;
        Animate(shift.Row, shift.From, shift.To, ShiftTime);

        // The animation is not trusted to hold its last value.
        DispatcherTimer.RunOnce(() =>
        {
            if (_shifts.TryGetValue(index, out var current) && ReferenceEquals(current, shift))
                OverlayAnimator.SetTranslation(shift.Row, 0, shift.To);
        }, ShiftTime + TimeSpan.FromMilliseconds(30));
    }

    private void End(bool commit)
    {
        _dragging = false;
        _armed = -1;
        if (_dragged is null)
            return;

        _settling = true;
        if (!commit)
            _to = _from;

        MakeRoom();
        var slot = (_to - _from) * _rowHeight;
        Animate(_dragged, _offset, slot, SettleTime);

        var from = _from;
        var to = _to;
        DispatcherTimer.RunOnce(() => Finish(from, to), SettleTime + TimeSpan.FromMilliseconds(30));
    }

    // Every row is back at its own place, and the list takes the new order in the same step, so no frame shows the old one.
    private void Finish(int from, int to)
    {
        try
        {
            foreach (var shift in _shifts.Values)
                OverlayAnimator.SetTranslation(shift.Row, 0, 0);

            if (_dragged is not null)
            {
                OverlayAnimator.SetTranslation(_dragged, 0, 0);
                Drop(_dragged);
            }

            if (from != to)
                _dropped(from, to);
        }
        finally
        {
            _shifts.Clear();
            _dragged = null;
            _settling = false;
            _ended();
        }
    }

    private static void Lift(Control row)
    {
        row.ZIndex = 10;
        if (row is ContentPresenter presenter)
        {
            presenter.Background = Palette.Pick("#FFFFFF", "#2A2C31");
            presenter.BoxShadow = BoxShadows.Parse("0 4 14 0 #38000000");
        }
    }

    private static void Drop(Control row)
    {
        row.ZIndex = 0;
        if (row is ContentPresenter presenter)
        {
            presenter.Background = null;
            presenter.BoxShadow = default;
        }
    }

    private static void Animate(Control row, double from, double to, TimeSpan duration)
    {
        if (ElementComposition.GetElementVisual(row) is not { } visual)
            return;

        visual.StopAnimation("Translation");
        var animation = visual.Compositor.CreateVector3DKeyFrameAnimation();
        animation.Target = "Translation";
        animation.InsertKeyFrame(0f, new Vector3D(0, from, 0));
        animation.InsertKeyFrame(1f, new Vector3D(0, to, 0), Ease);
        animation.Duration = duration;
        visual.StartAnimation("Translation", animation);
    }
}
