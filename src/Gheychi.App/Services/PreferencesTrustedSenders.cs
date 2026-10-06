using Gheychi.Core.Services;
using Gheychi.Core.Spam;

namespace Gheychi.App.Services;

/// <summary>
/// Kept by lookup key, so "+98 912 ...", "0912..." and "912..." are one sender. Held in memory and written through,
/// because the incoming-message path asks on every SMS.
/// </summary>
public sealed class PreferencesTrustedSenders : ITrustedSenders
{
    private const string Key = "spam_trusted_senders_v1";
    private const char Separator = '\n';

    private readonly object _gate = new();
    private readonly HashSet<string> _keys;

    public PreferencesTrustedSenders()
    {
        try
        {
            _keys = Preferences.Default.Get(Key, string.Empty)
                .Split(Separator, StringSplitOptions.RemoveEmptyEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Reading {Key} failed: {ex}");
            _keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public bool IsTrusted(string address)
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

    public void SetTrusted(string address, bool trusted)
    {
        var key = PhoneNumberNormalizer.ToLookupKey(address);
        if (key.Length == 0)
            return;

        lock (_gate)
        {
            var changed = trusted ? _keys.Add(key) : _keys.Remove(key);
            if (!changed)
                return;

            try
            {
                Preferences.Default.Set(Key, string.Join(Separator, _keys));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Saving {Key} failed: {ex}");
            }
        }
    }
}
