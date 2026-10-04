using Android.App;
using Android.Content;

namespace Gheychi.App.Platforms.Android.Receivers;

[BroadcastReceiver(
    Name = "com.evergreen.gheychiapp.MmsReceiver",
    Permission = "android.permission.BROADCAST_WAP_PUSH",
    Exported = true)]
[IntentFilter(
    ["android.provider.Telephony.WAP_PUSH_DELIVER"],
    DataMimeType = "application/vnd.wap.mms-message")]
public class MmsReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
    }
}
