using Android.Views;
using Gheychi.App.Controls;
using Gheychi.App.ViewModels;
using Microsoft.Maui.Controls;
using View = Microsoft.Maui.Controls.View;

namespace Gheychi.App.Behaviors;

public sealed record MessageBounds(double Y, double Height, double Width = 0);

public static class MessageHoldBehavior
{
    public static readonly BindableProperty IsEnabledProperty =
        BindableProperty.CreateAttached(
            "IsEnabled",
            typeof(bool),
            typeof(MessageHoldBehavior),
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
            nativeView.SetOnTouchListener(new MessageTouchListener(view, nativeView));
        }
    }

    private sealed class MessageTouchListener : Java.Lang.Object, Android.Views.View.IOnTouchListener
    {
        private readonly GestureDetector _gestureDetector;
        private bool _longPressFired;

        public MessageTouchListener(View view, Android.Views.View nativeView)
        {
            var listener = new MessageGestureListener(view, nativeView, () => _longPressFired = true);
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
                // Consume the rest of this finger's gesture (including UP) so the
                // view's Click — and therefore MAUI's TapGestureRecognizer — never
                // fires for a hold. Clear the pressed visual left behind by DOWN.
                if ((action == MotionEventActions.Up || action == MotionEventActions.Cancel) && v != null)
                {
                    try { v.Pressed = false; } catch { }
                }
                return true;
            }
            return false;
        }
    }

    private sealed class MessageGestureListener : GestureDetector.SimpleOnGestureListener
    {
        private readonly WeakReference<View> _viewRef;
        private readonly WeakReference<Android.Views.View> _nativeViewRef;
        private readonly Action _onFired;

        public MessageGestureListener(View view, Android.Views.View nativeView, Action onFired)
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
            while (p != null && p is not ChatView)
                p = p.Parent;

            if (p is ChatView chatView && view.BindingContext is ChatMessage msg)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    chatView.OnBubbleTappedDirect(msg);
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

            Element? p = view.Parent;
            while (p != null && p is not ChatView)
                p = p.Parent;

            if (p is ChatView cv && cv.Vm?.IsSelectionMode == true)
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

            int[] loc = new int[2];
            nativeView.GetLocationOnScreen(loc);
            var density = DeviceDisplay.Current.MainDisplayInfo.Density;
            if (density <= 0) density = 1;

            var y = loc[1] / density;
            var width = nativeView.Width / density;
            var height = nativeView.Height / density;

            if (p is ChatView chatView && view.BindingContext is ChatMessage msg)
            {
                // Mark the gesture consumed BEFORE dispatching, so this finger's
                // UP can't also trigger MAUI's TapGestureRecognizer (tap = SIM badge
                // toggle; hold = selection menu — never both for one touch).
                _onFired();
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    chatView.OpenSelectionMenu(msg, new MessageBounds(y, height, width));
                });
            }
        }
    }
#endif
}
