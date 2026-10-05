using Android.App;
using Android.Content;
using Android.Widget;
using Gheychi.App.Localization;
using Gheychi.App.Platforms.Android.Notifications;
using Gheychi.App.Platforms.Android.Services;
using Gheychi.Core.Models;
using Gheychi.Core.Services;
using AndroidXRemoteInput = AndroidX.Core.App.RemoteInput;

namespace Gheychi.App.Platforms.Android.Receivers;

/// <summary>The "Mark as read" and inline "Reply" buttons of a message notification.</summary>
[BroadcastReceiver(
    Name = "com.evergreen.gheychiapp.NotificationActionReceiver",
    Exported = false)]
public sealed class NotificationActionReceiver : BroadcastReceiver
{
    public const string ActionMarkRead = "com.evergreen.gheychiapp.NOTIFICATION_MARK_READ";
    public const string ActionReply = "com.evergreen.gheychiapp.NOTIFICATION_REPLY";

    /// <summary>How long a reply waits for the radio before the notification is dismissed anyway; the send itself carries on.</summary>
    private static readonly TimeSpan ReplyWait = TimeSpan.FromSeconds(4);

    /// <summary>Raised on the main thread after a notification action changed stored messages.</summary>
    public static event Action? ThreadsChanged;

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null)
            return;

        var threadId = intent.GetLongExtra(NotificationIds.ExtraThreadId, 0);
        if (threadId <= 0)
            return;

        var action = intent.Action;
        var address = intent.GetStringExtra(NotificationIds.ExtraAddress) ?? string.Empty;
        var subId = intent.GetIntExtra(NotificationIds.ExtraSubId, -1);
        var replyText = action == ActionReply ? ReadReplyText(intent) : null;

        var pending = GoAsync();
        Task.Run(async () =>
        {
            try
            {
                if (action == ActionMarkRead)
                    MarkRead(context, threadId);
                else if (action == ActionReply)
                    await ReplyAsync(context, threadId, address, subId, replyText);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Notification action failed: {ex}");
            }
            finally
            {
                pending?.Finish();
            }
        });
    }

    private static string? ReadReplyText(Intent intent)
    {
        var text = AndroidXRemoteInput.GetResultsFromIntent(intent)?.GetCharSequence(MessageNotifier.ReplyKey)?.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static void MarkRead(Context context, long threadId)
    {
        AndroidSmsService.MarkThreadRead(context, threadId);
        MainThread.BeginInvokeOnMainThread(RaiseThreadsChanged);
    }

    private static async Task ReplyAsync(Context context, long threadId, string address, int subId, string? text)
    {
        if (text is null || address.Length == 0)
        {
            MessageNotifier.Refresh(context, threadId, address, subId);
            return;
        }

        var sms = IPlatformApplication.Current?.Services.GetService<ISmsService>();
        var result = SmsSendResult.Failed;
        if (sms is not null)
        {
            // SendSmsAsync stores the row and hands it to the radio first; waiting longer only learns the outcome.
            var send = sms.SendSmsAsync(address, text, subId);
            var finished = await Task.WhenAny(send, Task.Delay(ReplyWait));
            result = finished == send ? send.Result : SmsSendResult.Pending;
        }

        if (result == SmsSendResult.Failed)
        {
            // Left unread and shown again, so the notification does not stay stuck on its progress spinner.
            MessageNotifier.Refresh(context, threadId, address, subId);
            MainThread.BeginInvokeOnMainThread(() =>
                Toast.MakeText(context, LocalizationManager.Instance["Notification_ReplyFailed"], ToastLength.Long)?.Show());
        }
        else
        {
            AndroidSmsService.MarkThreadRead(context, threadId);
        }

        MainThread.BeginInvokeOnMainThread(RaiseThreadsChanged);
    }

    private static void RaiseThreadsChanged()
    {
        try
        {
            ThreadsChanged?.Invoke();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ThreadsChanged handler failed: {ex}");
        }
    }
}
