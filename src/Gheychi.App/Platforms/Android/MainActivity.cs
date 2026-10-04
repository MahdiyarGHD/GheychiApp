using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Speech;
using Android.Views;

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

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Window?.SetSoftInputMode(SoftInput.AdjustResize);
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
