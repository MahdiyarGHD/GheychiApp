using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Android.Speech;
using Android.Views;
using AndroidX.Core.View;
using Avalonia.Android;
using Gheychi.App.Gestures;
using Gheychi.App.Platforms.Android.Notifications;

namespace Gheychi.App;

[Activity(
    Label = "Gheychi",
    Theme = "@style/Gheychi.Splash",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    WindowSoftInputMode = SoftInput.AdjustResize,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.LayoutDirection)]
[IntentFilter(
    [Intent.ActionSendto, Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataSchemes = ["sms", "smsto", "mms", "mmsto"])]
// The share sheet: a text sent to a conversation.
[IntentFilter(
    [Intent.ActionSend],
    Categories = [Intent.CategoryDefault],
    DataMimeType = "text/plain")]
public class MainActivity : AvaloniaMainActivity
{
    public const int VoiceSearchRequestCode = 7301;
    public const int DefaultSmsRequestCode = 7302;

    /// <summary>Raised whenever the app comes back to the front, e.g. from a system prompt such as the default-SMS-app dialog.</summary>
    public static event Action? Resumed;

    /// <summary>Raised with the recognised text, or null when the user cancelled / nothing was heard.</summary>
    public static event Action<string?>? VoiceSearchCompleted;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // The first view is built during base.OnCreate, and has to know a notified chat is coming.
        ChatLaunchRequests.FromIntent(Intent);
        SpamTabRequests.FromIntent(Intent);
        ExternalSendRequests.FromIntent(Intent);

        Platform.CurrentActivity = this;
        base.OnCreate(savedInstanceState);
        ApplySystemBarIcons();
    }

    /// <summary>
    /// Replaces the splash behind the app with the app's own background. The window shows through wherever the app has
    /// not drawn yet: the keyboard's place while it slides up was the splash's colour, a white flash.
    /// </summary>
    public static void UseAppBackground() =>
        Platform.CurrentActivity?.Window?.SetBackgroundDrawable(
            new Android.Graphics.Drawables.ColorDrawable(global::Android.Graphics.Color.ParseColor(ThemeState.IsDark ? "#121212" : "#F1F3F4")));

    // The bars are drawn over the app's own background, so their icons follow the theme in use.
    private void ApplySystemBarIcons()
    {
        if (Window is not { DecorView: { } decor } window)
            return;

        var controller = WindowCompat.GetInsetsController(window, decor);
        controller.AppearanceLightStatusBars = !ThemeState.IsDark;
        controller.AppearanceLightNavigationBars = !ThemeState.IsDark;
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        ChatLaunchRequests.FromIntent(intent);
        SpamTabRequests.FromIntent(intent);
        ExternalSendRequests.FromIntent(intent);
    }

    protected override void OnResume()
    {
        base.OnResume();
        Platform.CurrentActivity = this;
        ChatPresence.AppVisibilityChanged(true);
        ChatLaunchRequests.RaiseIfPending();
        SpamTabRequests.RaiseIfPending();
        ExternalSendRequests.RaiseIfPending();
        Resumed?.Invoke();
    }

    protected override void OnPause()
    {
        ChatPresence.AppVisibilityChanged(false);
        base.OnPause();
    }

    protected override void OnDestroy()
    {
        if (ReferenceEquals(Platform.CurrentActivity, this))
            Platform.CurrentActivity = null;
        base.OnDestroy();
    }

    public override bool DispatchTouchEvent(MotionEvent? ev)
    {
        UserActivity.Touched();
        return base.DispatchTouchEvent(ev);
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        Permissions.OnRequestPermissionsResult(requestCode);
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
