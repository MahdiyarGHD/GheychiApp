using Gheychi.Core.Notifications;

namespace Gheychi.App.Services;

/// <summary>
/// Per-conversation choices kept in the app preferences. Each map is held in memory and written through, because
/// the incoming-message path asks about the snooze on every SMS and must not parse preferences each time.
/// </summary>
public sealed class PreferencesThreadSettings : IThreadSettings
{
    private const string SnoozeKey = "thread_snooze_v1";
    private const string SimKey = "thread_preferred_sim_v1";

    private readonly object _gate = new();
    private readonly Dictionary<long, long> _snoozedUntil;
    private readonly Dictionary<long, int> _preferredSim;

    public PreferencesThreadSettings()
    {
        _snoozedUntil = Load<long>(SnoozeKey, (string text, out long value) => long.TryParse(text, out value));
        _preferredSim = Load<int>(SimKey, (string text, out int value) => int.TryParse(text, out value));
    }

    public long GetSnoozedUntil(long threadId)
    {
        lock (_gate)
            return _snoozedUntil.GetValueOrDefault(threadId);
    }

    public void SetSnoozedUntil(long threadId, long untilMillis)
    {
        lock (_gate)
        {
            if (untilMillis > 0)
                _snoozedUntil[threadId] = untilMillis;
            else
                _snoozedUntil.Remove(threadId);

            // A snooze that already ended carries no information; do not let the list grow forever.
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            foreach (var expired in _snoozedUntil.Where(p => !SnoozeSchedule.IsActive(p.Value, now)).Select(p => p.Key).ToList())
                _snoozedUntil.Remove(expired);

            Save(SnoozeKey, _snoozedUntil);
        }
    }

    public int GetPreferredSubId(long threadId)
    {
        lock (_gate)
            return _preferredSim.GetValueOrDefault(threadId);
    }

    public void SetPreferredSubId(long threadId, int subId)
    {
        lock (_gate)
        {
            if (subId > 0)
                _preferredSim[threadId] = subId;
            else
                _preferredSim.Remove(threadId);

            Save(SimKey, _preferredSim);
        }
    }

    private delegate bool TryParse<T>(string text, out T value);

    private static Dictionary<long, T> Load<T>(string key, TryParse<T> parse)
    {
        var map = new Dictionary<long, T>();
        try
        {
            var raw = Preferences.Default.Get(key, string.Empty);
            foreach (var entry in raw.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = entry.Split(':');
                if (parts.Length == 2 && long.TryParse(parts[0], out var id) && parse(parts[1], out var value))
                    map[id] = value;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Reading {key} failed: {ex}");
        }

        return map;
    }

    private static void Save<T>(string key, Dictionary<long, T> map)
    {
        try
        {
            Preferences.Default.Set(key, string.Join(',', map.Select(p => $"{p.Key}:{p.Value}")));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Saving {key} failed: {ex}");
        }
    }
}
