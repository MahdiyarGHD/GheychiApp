using Gheychi.App.Localization;
using Gheychi.Core.Notifications;

namespace Gheychi.App.Ui;

internal static class MuteDialogs
{
    /// <summary>Asks how long to mute notifications and returns the UTC end timestamp, or null when cancelled.</summary>
    public static async Task<long?> AskDurationMillisAsync()
    {
        var loc = LocalizationManager.Instance;
        var presets = SnoozeSchedule.Presets;
        var labels = presets.Select(p => loc[LabelKey(p)]).ToArray();

        var choice = await Dialogs.ActionSheetAsync(loc["Profile_SnoozeTitle"], loc["Chat_Cancel"], null, labels);
        var index = Array.IndexOf(labels, choice);
        return index < 0 ? null : SnoozeSchedule.UntilMillis(presets[index], DateTimeOffset.Now);
    }

    public static bool IsMuted(IThreadSettings? settings, long threadId) =>
        settings is not null &&
        SnoozeSchedule.IsActive(settings.GetSnoozedUntil(threadId), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    private static string LabelKey(SnoozePreset preset) => preset switch
    {
        SnoozePreset.OneHour => "Snooze_OneHour",
        SnoozePreset.EightHours => "Snooze_EightHours",
        SnoozePreset.TomorrowMorning => "Snooze_TomorrowMorning",
        SnoozePreset.OneWeek => "Snooze_OneWeek",
        _ => "Snooze_Forever"
    };
}
