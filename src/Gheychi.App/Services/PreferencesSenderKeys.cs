using Gheychi.Core.Services;

namespace Gheychi.App.Services;

/// <summary>
/// A set of senders kept by lookup key, so "+98 912 ...", "0912..." and "912..." are one sender. Held in memory and
/// written through, because the incoming-message path asks on every SMS.
/// </summary>
internal sealed class PreferencesSenderKeys
{
    private const char Separator = '\n';

    private readonly string _preferenceKey;
    private readonly object _gate = new();
    private readonly HashSet<string> _keys;

    public PreferencesSenderKeys(string preferenceKey)
    {
        _preferenceKey = preferenceKey;
        try
        {
            _keys = Preferences.Default.Get(preferenceKey, string.Empty)
                .Split(Separator, StringSplitOptions.RemoveEmptyEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Reading {preferenceKey} failed: {ex}");
            _keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public bool Contains(string address)
    {
        var key = PhoneNumberNormalizer.ToLookupKey(address);
        lock (_gate)
            return key.Length > 0 && _keys.Contains(key);
    }

    public IReadOnlyList<string> GetAll()
    {
        lock (_gate)
            return [.. _keys];
    }

    /// <summary>Whether the set changed.</summary>
    public bool Set(string address, bool included)
    {
        var key = PhoneNumberNormalizer.ToLookupKey(address);
        if (key.Length == 0)
            return false;

        lock (_gate)
        {
            var changed = included ? _keys.Add(key) : _keys.Remove(key);
            if (!changed)
                return false;

            try
            {
                Preferences.Default.Set(_preferenceKey, string.Join(Separator, _keys));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Saving {_preferenceKey} failed: {ex}");
            }

            return true;
        }
    }
}
