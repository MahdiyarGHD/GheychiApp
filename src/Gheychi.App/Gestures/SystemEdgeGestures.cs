using Android.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Gheychi.App.Gestures;

/// <summary>
/// Detects touches inside the left/right screen-edge strips used by Android's system Back gesture so starting a back
/// swipe never triggers a row or message hold-to-select.
/// </summary>
internal static class SystemEdgeGestures
{
    private const double MinBackEdgeDip = 28;

    public static bool IsInBackEdge(Control host, Point positionInHost)
    {
        var top = TopLevel.GetTopLevel(host);
        var (left, right) = GetHorizontalBackInsets(top);
        if (left <= 0 && right <= 0)
            return false;

        return MatchesHorizontalEdges(host, top, positionInHost, left, right);
    }

    public static bool IsNearHorizontalEdge(Control host, Point positionInHost, double marginDip = MinBackEdgeDip) =>
        MatchesHorizontalEdges(host, TopLevel.GetTopLevel(host), positionInHost, marginDip, marginDip);

    private static bool MatchesHorizontalEdges(Control host, TopLevel? top, Point positionInHost, double leftEdge, double rightEdge)
    {
        var width = top is { Bounds.Width: > 0 } ? top.Bounds.Width : host.Bounds.Width;
        if (width <= 0)
            return false;

        var x = top is Visual rootVisual
            ? host.TranslatePoint(positionInHost, rootVisual)?.X ?? positionInHost.X
            : positionInHost.X;

        return (leftEdge > 0 && x <= leftEdge) || (rightEdge > 0 && x >= width - rightEdge);
    }

    private static (double Left, double Right) GetHorizontalBackInsets(TopLevel? top)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(29))
            return (MinBackEdgeDip, MinBackEdgeDip);

        try
        {
            if (Platform.CurrentActivity?.Window?.DecorView?.RootWindowInsets is { } insets)
            {
                var scale = top?.RenderScaling ?? 1.0;
                if (scale <= 0)
                    scale = 1.0;

                double leftPx;
                double rightPx;
                if (OperatingSystem.IsAndroidVersionAtLeast(30))
                {
                    var sys = insets.GetInsets(WindowInsets.Type.SystemGestures());
                    leftPx = sys.Left;
                    rightPx = sys.Right;
                }
                else
                {
#pragma warning disable CA1422
                    var sys = insets.SystemGestureInsets;
                    leftPx = sys.Left;
                    rightPx = sys.Right;
#pragma warning restore CA1422
                }

                // In 3-button navigation both insets are 0; in gesture navigation Android reports > 0 for the edge strips.
                if (leftPx <= 0 && rightPx <= 0)
                    return (0, 0);

                return (
                    leftPx > 0 ? Math.Max(MinBackEdgeDip, leftPx / scale) : 0,
                    rightPx > 0 ? Math.Max(MinBackEdgeDip, rightPx / scale) : 0);
            }
        }
        catch
        {
        }

        return (MinBackEdgeDip, MinBackEdgeDip);
    }
}
