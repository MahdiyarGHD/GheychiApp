using System.Text.Json;
using Gheychi.Core.Spam;

namespace Gheychi.App.Services;

public sealed class PreferencesSpamModelUpdateState : ISpamModelUpdateState
{
    private const string LastCheckedKey = "spam_model_last_checked_v1";
    private const string AvailableKey = "spam_model_available_v1";

    public DateTime? LastCheckedUtc
    {
        get => Preferences.Default.ContainsKey(LastCheckedKey)
            ? DateTime.SpecifyKind(Preferences.Default.Get(LastCheckedKey, DateTime.MinValue), DateTimeKind.Utc)
            : null;
        set
        {
            if (value is { } time)
                Preferences.Default.Set(LastCheckedKey, time);
            else
                Preferences.Default.Remove(LastCheckedKey);
        }
    }

    public SpamModelRelease? Available
    {
        get
        {
            var json = Preferences.Default.Get(AvailableKey, string.Empty);
            if (json.Length == 0)
                return null;

            try
            {
                return JsonSerializer.Deserialize<SpamModelRelease>(json);
            }
            catch (JsonException)
            {
                return null;
            }
        }
        set
        {
            if (value is null)
                Preferences.Default.Remove(AvailableKey);
            else
                Preferences.Default.Set(AvailableKey, JsonSerializer.Serialize(value));
        }
    }
}
