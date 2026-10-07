using Gheychi.Core.Spam;

namespace Gheychi.App.Services;

/// <summary>The daily spam summary's settings, and when it last ran and the Spam tab was last opened.</summary>
public static class SpamDigestPreferences
{
    private const string EnabledKey = "spam_digest_enabled_v1";
    private const string MinuteOfDayKey = "spam_digest_minute_v1";
    private const string LastRunKey = "spam_digest_last_run_v1";
    private const string SeenKey = "spam_digest_seen_v1";

    public static bool Enabled
    {
        get => Preferences.Default.Get(EnabledKey, true);
        set => Preferences.Default.Set(EnabledKey, value);
    }

    /// <summary>Local time of day the summary runs, in minutes after midnight.</summary>
    public static int MinuteOfDay
    {
        get => Preferences.Default.Get(MinuteOfDayKey, SpamDigests.DefaultMinuteOfDay);
        set => Preferences.Default.Set(MinuteOfDayKey, Math.Clamp(value, 0, 24 * 60 - 1));
    }

    public static DateTime? LastRun
    {
        get => Get(LastRunKey);
        set => Set(LastRunKey, value);
    }

    public static DateTime? SeenAt
    {
        get => Get(SeenKey);
        set => Set(SeenKey, value);
    }

    // Stored as UTC ticks so a time-zone change does not move them.
    private static DateTime? Get(string key)
    {
        var ticks = Preferences.Default.Get(key, 0L);
        return ticks <= 0 ? null : new DateTime(ticks, DateTimeKind.Utc).ToLocalTime();
    }

    private static void Set(string key, DateTime? value)
    {
        if (value is { } v)
            Preferences.Default.Set(key, v.ToUniversalTime().Ticks);
        else
            Preferences.Default.Remove(key);
    }
}
