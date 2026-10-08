using Android.Views;
using AndroidX.Core.View;
using AView = Android.Views.View;

namespace Gheychi.App.Platforms.Android;

/// <summary>
/// Keeps a tab's page from being drawn once without its status-bar padding when the tab is opened. MAUI pads a page
/// for the system bars from where the page sits on screen when window insets are dispatched, and only asks for that
/// dispatch after the page has been laid out. A page shown by a tab switch was therefore drawn once with its content
/// under the status bar, then dropped down into place on the next frame.
/// </summary>
internal static class TabPageInsets
{
    public static void Attach(Page page) =>
        page.HandlerChanged += (_, _) =>
        {
            if (page.Handler?.PlatformView is AView view)
                _ = new Guard(view);
        };

    private sealed class Guard : Java.Lang.Object, ViewTreeObserver.IOnPreDrawListener, AView.IOnAttachStateChangeListener
    {
        private readonly AView _view;
        private ViewTreeObserver? _observer;
        private bool _shown;

        public Guard(AView view)
        {
            _view = view;
            view.AddOnAttachStateChangeListener(this);
            if (view.IsAttachedToWindow)
                OnViewAttachedToWindow(view);
        }

        public void OnViewAttachedToWindow(AView? attached)
        {
            _shown = false;
            _observer = _view.ViewTreeObserver;
            if (_observer is { IsAlive: true })
                _observer.AddOnPreDrawListener(this);
        }

        public void OnViewDetachedFromWindow(AView? detached)
        {
            if (_observer is { IsAlive: true })
                _observer.RemoveOnPreDrawListener(this);
            _observer = null;
        }

        // Runs before every frame of the window. Only the first frame after the page becomes visible is cancelled:
        // the dispatch asked for here runs at the start of the next frame, from the layout just done, so the frame
        // that reaches the screen already has the padding.
        public bool OnPreDraw()
        {
            try
            {
                var shown = _view.IsShown;
                if (shown == _shown)
                    return true;

                _shown = shown;
                if (!shown)
                    return true;

                ViewCompat.RequestApplyInsets(_view);
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Tab page insets failed: {ex}");
                return true;
            }
        }
    }
}
