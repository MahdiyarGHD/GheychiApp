using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Speech;
using Android.Views;
using Gheychi.App.Gestures;
using Gheychi.App.Platforms.Android.Notifications;
using Gheychi.Core.Services;

namespace Gheychi.App;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    WindowSoftInputMode = SoftInput.AdjustResize,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
[IntentFilter(
    [Intent.ActionSend, Intent.ActionSendto],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataSchemes = ["sms", "smsto", "mms", "mmsto"])]
public class MainActivity : MauiAppCompatActivity
{
    public const int VoiceSearchRequestCode = 7301;

    /// <summary>Raised with the recognised text, or null when the user cancelled / nothing was heard.</summary>
    public static event Action<string?>? VoiceSearchCompleted;

    private HorizontalSwipeTracker? _swipe;
    private VelocityTracker? _velocity;
    private IPageSwipeClient? _swipeClient;
    private float _density = 1;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Window?.SetSoftInputMode(SoftInput.AdjustResize);

        _density = Resources?.DisplayMetrics?.Density ?? 1;
        var slop = ViewConfiguration.Get(this)?.ScaledTouchSlop ?? 24;
        _swipe = new HorizontalSwipeTracker(slop);

        ChatLaunchRequests.FromIntent(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        ChatLaunchRequests.FromIntent(intent);
    }

    protected override void OnResume()
    {
        base.OnResume();
        ChatPresence.AppVisibilityChanged(true);
        ChatLaunchRequests.RaiseIfPending();
    }

    protected override void OnPause()
    {
        ChatPresence.AppVisibilityChanged(false);
        base.OnPause();
    }

    public override bool DispatchTouchEvent(MotionEvent? ev)
    {
        UserActivity.Touched();

        if (ev is null || _swipe is null)
            return base.DispatchTouchEvent(ev);

        try
        {
            switch (ev.ActionMasked)
            {
                case MotionEventActions.Down:
                    EndSwipeTracking();
                    _swipeClient = PageSwipe.Client;
                    if (_swipeClient is not null)
                    {
                        _swipe.Begin(ev.RawX, ev.RawY);
                        _velocity = VelocityTracker.Obtain();
                        _velocity?.AddMovement(ev);
                    }
                    break;

                case MotionEventActions.Move when _swipeClient is not null:
                    _velocity?.AddMovement(ev);
                    var wasDragging = _swipe.IsDragging;
                    if (_swipe.Move(ev.RawX, ev.RawY, _swipeClient.AllowedSwipeSign))
                    {
                        if (!wasDragging)
                        {
                            // Views that already received the touch (list scroll, long-press) drop it.
                            var cancel = MotionEvent.Obtain(ev);
                            cancel.Action = MotionEventActions.Cancel;
                            base.DispatchTouchEvent(cancel);
                            cancel.Recycle();
                            _swipeClient.OnSwipeStarted();
                        }

                        _swipeClient.OnSwipeMoved(_swipe.Offset / _density);
                        return true;
                    }
                    break;

                case MotionEventActions.Up:
                case MotionEventActions.Cancel:
                    if (_swipeClient is not null && _swipe.IsDragging)
                    {
                        _velocity?.AddMovement(ev);
                        _velocity?.ComputeCurrentVelocity(1000);
                        var velocity = (_velocity?.XVelocity ?? 0) / _density;
                        var client = _swipeClient;
                        var offset = _swipe.Offset / _density;
                        EndSwipeTracking();
                        client.OnSwipeEnded(offset, velocity, ev.ActionMasked == MotionEventActions.Cancel);
                        return true;
                    }

                    EndSwipeTracking();
                    break;
            }
        }
        catch (Exception ex)
        {
            // A broken swipe must never take normal touch handling down with it.
            System.Diagnostics.Debug.WriteLine($"Page swipe failed: {ex}");
            EndSwipeTracking();
        }

        return base.DispatchTouchEvent(ev);
    }

    private void EndSwipeTracking()
    {
        _swipe?.Reset();
        _swipeClient = null;
        _velocity?.Recycle();
        _velocity = null;
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);

        if (requestCode != VoiceSearchRequestCode)
            return;

        string? text = null;
        if (resultCode == Result.Ok)
            text = data?.GetStringArrayListExtra(RecognizerIntent.ExtraResults)?.FirstOrDefault();

        VoiceSearchCompleted?.Invoke(text);
    }
}
