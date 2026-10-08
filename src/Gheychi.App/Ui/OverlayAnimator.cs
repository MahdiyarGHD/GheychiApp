using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Rendering.Composition;

namespace Gheychi.App.Ui;

/// <summary>
/// Slides and fades a full-screen overlay on the render thread. The animation is handed to the compositor and runs
/// there: filling a list, a layout pass or a GC on the UI thread while it plays no longer freezes or delays it.
/// It drives <c>Translation</c>, which layout never rewrites, not <c>Offset</c>.
/// </summary>
public static class OverlayAnimator
{
    public static readonly TimeSpan OpenDuration = TimeSpan.FromMilliseconds(200);
    public static readonly TimeSpan CloseDuration = TimeSpan.FromMilliseconds(160);

    private static readonly IEasing Decelerate = new CubicEaseOut();
    private static readonly IEasing Accelerate = new CubicEaseIn();

    /// <summary>Lets a layout change before an open (one frame) finish, so it is not part of the slide.</summary>
    public static Task SettleAsync() => Task.Delay(16);

    public static Task SlideYAsync(Control overlay, double fromDip, double toDip, TimeSpan duration, bool decelerate) =>
        Run(overlay, new Vector3D(0, fromDip, 0), new Vector3D(0, toDip, 0), duration, decelerate);

    public static Task SlideXAsync(Control overlay, double fromDip, double toDip, TimeSpan duration, bool decelerate) =>
        Run(overlay, new Vector3D(fromDip, 0, 0), new Vector3D(toDip, 0, 0), duration, decelerate);

    /// <summary>Puts the overlay at a position without animating, e.g. parking it far off screen or following a finger.</summary>
    public static void SetTranslation(Control overlay, double xDip, double yDip)
    {
        if (ElementComposition.GetElementVisual(overlay) is { } visual)
        {
            visual.StopAnimation("Translation");
            visual.Translation = new Vector3D(xDip, yDip, 0);
        }
    }

    public static async Task FadeAsync(Control control, double from, double to, TimeSpan duration)
    {
        if (ElementComposition.GetElementVisual(control) is not { } visual)
        {
            control.Opacity = to;
            return;
        }

        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.Target = "Opacity";
        animation.InsertKeyFrame(0f, (float)from);
        animation.InsertKeyFrame(1f, (float)to, to > from ? Decelerate : Accelerate);
        animation.Duration = duration;
        visual.StartAnimation("Opacity", animation);

        await Task.Delay(duration + TimeSpan.FromMilliseconds(20));
        visual.Opacity = (float)to;
        control.Opacity = to;
    }

    private static async Task Run(Control overlay, Vector3D from, Vector3D to, TimeSpan duration, bool decelerate)
    {
        if (ElementComposition.GetElementVisual(overlay) is not { } visual)
            return;

        visual.StopAnimation("Translation");
        visual.Translation = from;

        var animation = visual.Compositor.CreateVector3DKeyFrameAnimation();
        animation.Target = "Translation";
        animation.InsertKeyFrame(0f, from);
        animation.InsertKeyFrame(1f, to, decelerate ? Decelerate : Accelerate);
        animation.Duration = duration;
        visual.StartAnimation("Translation", animation);

        // The animation has no completion callback worth waiting on; the end value is written explicitly.
        await Task.Delay(duration + TimeSpan.FromMilliseconds(20));
        visual.StopAnimation("Translation");
        visual.Translation = to;
    }
}
