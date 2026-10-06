namespace Gheychi.App.Gestures;

/// <summary>
/// Slides a full-screen overlay in and out. On Android the slide is a platform view-property animation:
/// cheaper per frame than MAUI's managed animation (no managed code, no re-layout per frame), but its
/// frames are still produced on the UI thread. Anything heavy on the UI thread (filling a list, a layout
/// pass, a GC) delays the start or freezes the slide, so callers do that work before the slide starts
/// only if it is cheap, and otherwise after it ends.
/// </summary>
public static class OverlayAnimator
{
    /// <summary>Distance that keeps a parked overlay far outside the screen on any device.</summary>
    public const double ParkedDistance = 3000;

    public static async Task SlideYAsync(VisualElement overlay, double fromDp, double toDp, uint duration, bool decelerate)
    {
        var animated = false;
#if ANDROID
        animated = await TryNativeSlideAsync(overlay, fromDp, toDp, duration, decelerate);
#endif
        if (!animated)
        {
            SetTranslationY(overlay, fromDp);
            await overlay.TranslateToAsync(0, toDp, duration, decelerate ? Easing.CubicOut : Easing.CubicIn);
        }

        SetTranslationY(overlay, toDp);
    }

    /// <summary>Sets the MAUI property and the platform view together; the platform view otherwise keeps its last animated value.</summary>
    public static void SetTranslationY(VisualElement overlay, double dp)
    {
        overlay.TranslationY = dp;
#if ANDROID
        if (overlay.Handler?.PlatformView is Android.Views.View native)
            native.TranslationY = (float)(dp * (native.Resources?.DisplayMetrics?.Density ?? 1f));
#endif
    }

#if ANDROID
    private static async Task<bool> TryNativeSlideAsync(VisualElement overlay, double fromDp, double toDp, uint duration, bool decelerate)
    {
        if (overlay.Handler?.PlatformView is not Android.Views.View native || !native.IsAttachedToWindow)
            return false;

        var animator = native.Animate();
        if (animator is null)
            return false;

        var density = native.Resources?.DisplayMetrics?.Density ?? 1f;
        var finished = new TaskCompletionSource();

        animator.Cancel();
        native.TranslationY = (float)(fromDp * density);
        animator.SetDuration(duration);
        if (decelerate)
            animator.SetInterpolator(new Android.Views.Animations.DecelerateInterpolator(2f));
        else
            animator.SetInterpolator(new Android.Views.Animations.AccelerateInterpolator(1.4f));
        animator.TranslationY((float)(toDp * density));
        animator.WithEndAction(new Java.Lang.Runnable(() => finished.TrySetResult()));
        animator.Start();

        // The end action is skipped when the animation is cancelled; never wait on it forever.
        await Task.WhenAny(finished.Task, Task.Delay((int)duration + 400));
        return true;
    }
#endif
}
