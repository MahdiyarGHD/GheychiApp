using Avalonia.Input;
using Gheychi.App.Localization;
using Gheychi.App.Platforms.Android.Services;
using Gheychi.App.Services;
using Gheychi.App.Ui;
using Gheychi.App.ViewModels;
using Gheychi.Core.Scheduling;

namespace Gheychi.App.Controls;

// Sending later: a long press on the send button schedules the message, and the bars above the box show what waits.
public partial class ChatView
{
    // Releasing the finger that held the send button also reports a tap, which would send what was just scheduled.
    private bool _sendHoldFired;

    private void OnSendPressed(object? sender, PointerPressedEventArgs e) => _sendHoldFired = false;

    private async void OnSendHolding(object? sender, HoldingRoutedEventArgs e)
    {
        if (e.HoldingState != HoldingState.Started || Vm is not { CanReply: true } vm)
            return;

        _sendHoldFired = true;
        e.Handled = true;
        TriggerHaptic(global::Android.Views.FeedbackConstants.LongPress);
        CloseEmojiPanel();

        var loc = LocalizationManager.Instance;
        if (string.IsNullOrWhiteSpace(vm.Draft))
        {
            Toast.Show(loc["Chat_ScheduleEmpty"]);
            return;
        }

        if (await PickSendTimeAsync(vm) is not { } at || !ReferenceEquals(vm, Vm))
            return;

        await OfferExactAlarmsAsync(vm);
        if (!vm.ScheduleDraft(at))
            return;

        ReleaseInputFocus();
        Toast.Show(string.Format(loc[vm.Pending.CanFireExactly ? "Chat_Scheduled" : "Chat_ScheduledInexact"], vm.Pending.Describe(at)));
    }

    // Asked once: without the permission Android sends a scheduled message a few minutes late, and the user may prefer that.
    private static async Task OfferExactAlarmsAsync(ChatViewModel vm)
    {
        if (vm.Pending.CanFireExactly || AppPreferences.ExactAlarmAsked)
            return;

        AppPreferences.ExactAlarmAsked = true;
        var loc = LocalizationManager.Instance;
        if (await Dialogs.AlertAsync(loc["Chat_ExactAlarmTitle"], loc["Chat_ExactAlarmMessage"], loc["Chat_ExactAlarmOpen"], loc["Chat_ExactAlarmLater"]))
            ScheduledSendAlarm.OpenExactAlarmSettings(Platform.AppContext);
    }

    private void OnUndoSendTapped(object? sender, TappedEventArgs e) => Vm?.UndoHeld();

    private async void OnScheduledBarTapped(object? sender, TappedEventArgs e)
    {
        if (Vm is not { } vm)
            return;

        var loc = LocalizationManager.Instance;
        var scheduled = vm.Pending.Scheduled();
        if (scheduled.Count == 0)
            return;

        var labels = scheduled.Select(m => $"{vm.Pending.Describe(m.SendAtMillis)} · {PendingSends.Preview(m.Text)}").ToArray();
        var picked = await Dialogs.ActionSheetAsync(loc["Chat_ScheduledTitle"], loc["Chat_Cancel"], null, labels);
        var index = picked is null ? -1 : Array.IndexOf(labels, picked);
        if (index < 0)
            return;

        var message = scheduled[index];
        var sendNow = loc["Chat_ScheduledSendNow"];
        var change = loc["Chat_ScheduledChangeTime"];
        var delete = loc["Chat_ScheduledDelete"];
        var action = await Dialogs.ActionSheetAsync(PendingSends.Preview(message.Text), loc["Chat_Cancel"], null, sendNow, change, delete);
        if (action == sendNow)
            vm.Pending.SendNow(message.Id);
        else if (action == delete)
            vm.Pending.Delete(message.Id);
        else if (action == change && await PickSendTimeAsync(vm) is { } at)
        {
            await OfferExactAlarmsAsync(vm);
            vm.Pending.Reschedule(message.Id, at);
        }
    }

    /// <summary>A preset, or a day and a time from the system pickers; null when the user backed out or picked a time too close.</summary>
    private static async Task<long?> PickSendTimeAsync(ChatViewModel vm)
    {
        var loc = LocalizationManager.Instance;
        var now = DateTimeOffset.Now;
        var presets = SendSchedule.Presets(now);
        var labels = presets
            .Select(p => $"{loc[PresetKey(p)]} · {vm.Pending.Describe(SendSchedule.AtMillis(p, now))}")
            .Append(loc["Schedule_Custom"])
            .ToArray();

        var picked = await Dialogs.ActionSheetAsync(loc["Chat_ScheduleTitle"], loc["Chat_Cancel"], null, labels);
        var index = picked is null ? -1 : Array.IndexOf(labels, picked);
        if (index < 0)
            return null;

        long at;
        if (index < presets.Count)
        {
            at = SendSchedule.AtMillis(presets[index], now);
        }
        else
        {
            if (await DateTimeSheets.PickDateAsync(now) is not { } day || await DateTimeSheets.PickTimeAsync(now.Hour, now.Minute, DateTimeSheets.Is24Hour) is not { } time)
                return null;

            at = SendSchedule.AtMillis(day, time.Hour, time.Minute, now);
        }

        if (SendSchedule.IsAcceptable(at, DateTimeOffset.Now))
            return at;

        Toast.Show(loc["Chat_ScheduleTooSoon"]);
        return null;
    }

    private static string PresetKey(SchedulePreset preset) => preset switch
    {
        SchedulePreset.OneHour => "Schedule_OneHour",
        SchedulePreset.ThisEvening => "Schedule_ThisEvening",
        SchedulePreset.TomorrowMorning => "Schedule_TomorrowMorning",
        _ => "Schedule_TomorrowEvening"
    };
}
