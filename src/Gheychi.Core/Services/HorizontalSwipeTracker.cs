namespace Gheychi.Core.Services;

/// <summary>
/// Decides whether a touch stream is a horizontal page swipe or something else (a vertical list
/// scroll, a tap, a swipe the screen does not accept). Platform code feeds it raw coordinates.
/// </summary>
public sealed class HorizontalSwipeTracker
{
    private readonly double _slop;
    private readonly double _dominance;
    private double _startX;
    private double _startY;
    private int _sign;
    private bool _tracking;

    /// <param name="slop">Movement, in the caller's unit, before the gesture is classified.</param>
    /// <param name="dominance">How many times larger the horizontal travel must be than the vertical one.</param>
    public HorizontalSwipeTracker(double slop, double dominance = 1.5)
    {
        _slop = slop;
        _dominance = dominance;
    }

    public bool IsDragging { get; private set; }

    public bool IsRejected { get; private set; }

    /// <summary>Horizontal travel since the swipe was recognised, so a drag starts at zero instead of jumping by the slop.</summary>
    public double Offset { get; private set; }

    public void Begin(double x, double y)
    {
        _startX = x;
        _startY = y;
        _sign = 0;
        _tracking = true;
        IsDragging = false;
        IsRejected = false;
        Offset = 0;
    }

    public void Reset()
    {
        _tracking = false;
        IsDragging = false;
        IsRejected = false;
        Offset = 0;
    }

    /// <param name="allowedSign">-1 accepts only leftward swipes, +1 only rightward, 0 none.</param>
    /// <returns>True while the touch is a recognised swipe.</returns>
    public bool Move(double x, double y, int allowedSign)
    {
        if (!_tracking || IsRejected)
            return false;

        var dx = x - _startX;
        var dy = y - _startY;

        if (!IsDragging)
        {
            var adx = Math.Abs(dx);
            var ady = Math.Abs(dy);
            if (adx < _slop && ady < _slop)
                return false;

            if (allowedSign == 0 || adx < _dominance * ady || Math.Sign(dx) != allowedSign)
            {
                IsRejected = true;
                return false;
            }

            IsDragging = true;
            _sign = allowedSign;
        }

        Offset = dx - _sign * _slop;
        return true;
    }
}
