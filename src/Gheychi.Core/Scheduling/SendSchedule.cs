namespace Gheychi.Core.Scheduling;

public enum SchedulePreset
{
    OneHour,
    ThisEvening,
    TomorrowMorning,
    TomorrowEvening
}

public static class SendSchedule
{
    private const int MorningHour = 8;
    private const int EveningHour = 18;

    /// <summary>The shortest wait the picker accepts: a time any closer would be sent before the confirmation is read.</summary>
    public static readonly TimeSpan MinimumLead = TimeSpan.FromMinutes(1);

    /// <summary>The send delays offered in Settings, in seconds; 0 sends at once.</summary>
    public static IReadOnlyList<int> DelayOptions { get; } = [0, 3, 5, 7, 10, 15, 30];

    public static int NormalizeDelay(int seconds) => DelayOptions.Contains(seconds) ? seconds : 0;

    /// <summary>The presets that still lie ahead: this evening is not offered once it is nearly evening or past it.</summary>
    public static IReadOnlyList<SchedulePreset> Presets(DateTimeOffset now) =>
        now.Hour < EveningHour - 1
            ? [SchedulePreset.OneHour, SchedulePreset.ThisEvening, SchedulePreset.TomorrowMorning, SchedulePreset.TomorrowEvening]
            : [SchedulePreset.OneHour, SchedulePreset.TomorrowMorning, SchedulePreset.TomorrowEvening];

    /// <summary>When the preset falls, as unix milliseconds. <paramref name="now"/> carries the user's local offset.</summary>
    public static long AtMillis(SchedulePreset preset, DateTimeOffset now) => preset switch
    {
        SchedulePreset.OneHour => now.AddHours(1).ToUnixTimeMilliseconds(),
        SchedulePreset.ThisEvening => At(now, 0, EveningHour),
        SchedulePreset.TomorrowMorning => At(now, 1, MorningHour),
        _ => At(now, 1, EveningHour)
    };

    /// <summary>A picked day and time as unix milliseconds, in the offset of <paramref name="now"/>.</summary>
    public static long AtMillis(DateTime day, int hour, int minute, DateTimeOffset now) =>
        new DateTimeOffset(day.Date.AddHours(hour).AddMinutes(minute), now.Offset).ToUnixTimeMilliseconds();

    public static bool IsAcceptable(long atMillis, DateTimeOffset now) =>
        atMillis >= now.Add(MinimumLead).ToUnixTimeMilliseconds();

    private static long At(DateTimeOffset now, int days, int hour) =>
        new DateTimeOffset(now.Date.AddDays(days).AddHours(hour), now.Offset).ToUnixTimeMilliseconds();
}
