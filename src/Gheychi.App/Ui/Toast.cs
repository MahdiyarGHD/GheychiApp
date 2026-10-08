namespace Gheychi.App.Ui;

internal static class Toast
{
    public static void Show(string text) =>
        global::Android.Widget.Toast.MakeText(Platform.AppContext, text, global::Android.Widget.ToastLength.Short)?.Show();
}
