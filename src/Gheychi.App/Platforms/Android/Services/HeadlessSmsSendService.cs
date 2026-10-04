using Android.App;
using Android.Content;
using Android.OS;

namespace Gheychi.App.Platforms.Android.Services;

[Service(
    Name = "com.evergreen.gheychiapp.HeadlessSmsSendService",
    Permission = "android.permission.SEND_RESPOND_VIA_MESSAGE",
    Exported = true)]
[IntentFilter(
    ["android.intent.action.RESPOND_VIA_MESSAGE"],
    Categories = [Intent.CategoryDefault],
    DataSchemes = ["sms", "smsto", "mms", "mmsto"])]
public class HeadlessSmsSendService : Service
{
    public override IBinder? OnBind(Intent? intent) => null;
}
