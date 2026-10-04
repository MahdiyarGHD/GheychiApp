#if ANDROID
using Android.Views;
#endif
using Gheychi.App.Pages;
using Gheychi.App.ViewModels;
using Microsoft.Maui.Controls;
using View = Microsoft.Maui.Controls.View;

namespace Gheychi.App.Behaviors;

public static class ThreadHoldBehavior
{
    public static readonly BindableProperty IsEnabledProperty =
        BindableProperty.CreateAttached(
            "IsEnabled",
            typeof(bool),
            typeof(ThreadHoldBehavior),
            false,
            propertyChanged: OnIsEnabledChanged);

    public static bool GetIsEnabled(BindableObject view) => (bool)view.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(BindableObject view, bool value) => view.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not View view)
            return;

#if ANDROID
        view.HandlerChanged -= OnViewHandlerChanged;
        if ((bool)newValue)
        {
            view.HandlerChanged += OnViewHandlerChanged;
            AttachListener(view);
        }
#endif
    }

#if ANDROID
    private static void OnViewHandlerChanged(object? sender, EventArgs e)
    {
        if (sender is View view && GetIsEnabled(view))
            AttachListener(view);
    }

    private static void AttachListener(View view)
    {
        if (view.Handler?.PlatformView is Android.Views.View nativeView)
        {
            nativeView.Clickable = true;
            nativeView.SetOnTouchListener(new ThreadTouchListener(view, nativeView));

            if (nativeView is Android.Views.ViewGroup viewGroup)
            {
                SetChildrenNonClickable(viewGroup);
                viewGroup.ChildViewAdded += (s, e) =>
                {
                    if (e.Child != null)
                    {
                        e.Child.Clickable = false;
                        e.Child.LongClickable = false;
                        if (e.Child is Android.Views.ViewGroup sub)
                            SetChildrenNonClickable(sub);
                    }
                };
            }
        }
    }

    private static void SetChildrenNonClickable(Android.Views.ViewGroup vg)
    {
        for (int i = 0; i < vg.ChildCount; i++)
        {
            var child = vg.GetChildAt(i);
            if (child != null)
            {
                child.Clickable = false;
                child.LongClickable = false;
                if (child is Android.Views.ViewGroup nested)
                {
                    SetChildrenNonClickable(nested);
                }
            }
        }
    }

    private sealed class ThreadTouchListener : Java.Lang.Object, Android.Views.View.IOnTouchListener
    {
        private readonly GestureDetector _gestureDetector;
        private bool _longPressFired;

        public ThreadTouchListener(View view, Android.Views.View nativeView)
        {
            var listener = new ThreadGestureListener(view, nativeView, () => _longPressFired = true);
            _gestureDetector = new GestureDetector(nativeView.Context, listener);
        }

        public bool OnTouch(Android.Views.View? v, MotionEvent? e)
        {
            if (e == null)
                return false;

            var action = e.ActionMasked;
            if (action == MotionEventActions.Down)
                _longPressFired = false;

            _gestureDetector.OnTouchEvent(e);

            if (_longPressFired)
            {
                if ((action == MotionEventActions.Up || action == MotionEventActions.Cancel) && v != null)
                {
                    try { v.Pressed = false; } catch { }
                }
                return true;
            }

            return false;
        }
    }

    private sealed class ThreadGestureListener : GestureDetector.SimpleOnGestureListener
    {
        private readonly WeakReference<View> _viewRef;
        private readonly WeakReference<Android.Views.View> _nativeViewRef;
        private readonly Action _onFired;

        public ThreadGestureListener(View view, Android.Views.View nativeView, Action onFired)
        {
            _viewRef = new WeakReference<View>(view);
            _nativeViewRef = new WeakReference<Android.Views.View>(nativeView);
            _onFired = onFired;
        }

        public override bool OnDown(MotionEvent e) => true;

        public override bool OnSingleTapUp(MotionEvent e)
        {
            if (!_viewRef.TryGetTarget(out var view))
                return false;

            Element? p = view.Parent;
            while (p != null && p is not MessagesPage)
                p = p.Parent;

            if (p is MessagesPage page && view.BindingContext is ThreadItem thread)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    page.OnThreadRowTappedDirect(thread);
                });
                return true;
            }

            return false;
        }

        public override void OnLongPress(MotionEvent e)
        {
            if (!_viewRef.TryGetTarget(out var view) || !_nativeViewRef.TryGetTarget(out var nativeView))
                return;

            if (!nativeView.IsAttachedToWindow)
                return;

            var parent = nativeView.Parent;
            while (parent != null)
            {
                if (parent is AndroidX.RecyclerView.Widget.RecyclerView rv &&
                    rv.ScrollState != AndroidX.RecyclerView.Widget.RecyclerView.ScrollStateIdle)
                    return;
                parent = parent.Parent;
            }

            try
            {
                nativeView.PerformHapticFeedback(FeedbackConstants.LongPress);
            }
            catch { }

            Element? p = view.Parent;
            while (p != null && p is not MessagesPage)
                p = p.Parent;

            if (p is MessagesPage page && view.BindingContext is ThreadItem thread)
            {
                _onFired();
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    page.OnThreadRowHeldDirect(thread);
                });
            }
        }
    }
#endif
}
