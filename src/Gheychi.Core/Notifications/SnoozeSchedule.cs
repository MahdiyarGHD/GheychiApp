namespace Gheychi.Core.Notifications;

public enum SnoozePreset
{
    OneHour,
    EightHours,
    TomorrowMorning,
    OneWeek,
    Forever
}

public static class SnoozeSchedule
{
    /// <summary>Stored when a conversation is snoozed with no end.</summary>
    public const long Indefinite = long.MaxValue;

    private const int MorningHour = 8;

    public static IReadOnlyList<SnoozePreset> Presets { get; } =
        [SnoozePreset.OneHour, SnoozePreset.EightHours, SnoozePreset.TomorrowMorning, SnoozePreset.OneWeek, SnoozePreset.Forever];

    /// <summary>The moment the snooze ends, as unix milliseconds. <paramref name="now"/> carries the user's local offset.</summary>
    public static long UntilMillis(SnoozePreset preset, DateTimeOffset now) => preset switch
    {
        SnoozePreset.OneHour => now.AddHours(1).ToUnixTimeMilliseconds(),
        SnoozePreset.EightHours => now.AddHours(8).ToUnixTimeMilliseconds(),
        SnoozePreset.TomorrowMorning => new DateTimeOffset(now.Date.AddDays(1).AddHours(MorningHour), now.Offset).ToUnixTimeMilliseconds(),
        SnoozePreset.OneWeek => now.AddDays(7).ToUnixTimeMilliseconds(),
        _ => Indefinite
    };

    public static bool IsActive(long untilMillis, long nowMillis) => untilMillis > nowMillis;
}
