namespace Gheychi.App.Controls;

internal static class Toast
{
    public static void Show(string text)
    {
#if ANDROID
        global::Android.Widget.Toast.MakeText(Platform.AppContext, text, global::Android.Widget.ToastLength.Short)?.Show();
#endif
    }
}
