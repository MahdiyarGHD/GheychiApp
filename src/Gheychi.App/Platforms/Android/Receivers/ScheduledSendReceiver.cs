using Android.App;
using Android.Content;
using Gheychi.App.Platforms.Android.Services;
using Gheychi.Core.Models;
using Gheychi.Core.Scheduling;
using Gheychi.Core.Services;

namespace Gheychi.App.Platforms.Android.Receivers;

/// <summary>Sends a scheduled or delayed message when its alarm goes off. Not exported: only the app's own alarm may run it.</summary>
[BroadcastReceiver(Name = "com.evergreen.gheychiapp.ScheduledSendReceiver", Exported = false)]
public sealed class ScheduledSendReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action != ScheduledSendAlarm.ActionSend || intent.GetStringExtra(ScheduledSendAlarm.ExtraId) is not { } id)
            return;

        var pending = GoAsync();
        _ = Task.Run(async () =>
        {
            try
            {
                await SendAsync(id);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Sending a scheduled message failed: {ex}");
            }
            finally
            {
                pending?.Finish();
            }
        });
    }

    /// <summary>
    /// Sends the message if nobody else has: the chat's timer may have claimed it first. SendSmsAsync stores the row first,
    /// so a message that cannot go out is left in the conversation as a failed one the user can retry.
    /// </summary>
    internal static async Task<SmsSendResult?> SendAsync(string id)
    {
        var services = IPlatformApplication.Current?.Services;
        var store = services?.GetService<IScheduledSendStore>();
        var sms = services?.GetService<ISmsService>();
        if (store is null || sms is null || store.Claim(id) is not { } message)
            return null;

        var result = await sms.SendSmsAsync(message.Address, message.Text, message.SubId);
        MainThread.BeginInvokeOnMainThread(() => NotificationActionReceiver.RaiseThreadsChanged(message.ThreadId));
        return result;
    }
}
