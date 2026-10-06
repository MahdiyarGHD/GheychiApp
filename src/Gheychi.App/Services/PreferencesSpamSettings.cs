using Gheychi.Core.Spam;

namespace Gheychi.App.Services;

public sealed class PreferencesSpamSettings : ISpamSettings
{
    private const string EnabledKey = "spam_enabled_v1";
    private const string ThresholdKey = "spam_threshold_v1";
    private const string RetentionDaysKey = "spam_retention_days_v1";

    public bool Enabled
    {
        get => Preferences.Default.Get(EnabledKey, true);
        set => Preferences.Default.Set(EnabledKey, value);
    }

    public float Threshold
    {
        get => Preferences.Default.Get(ThresholdKey, SpamDetector.DefaultThreshold);
        set => Preferences.Default.Set(ThresholdKey, Math.Clamp(value, 0f, 1f));
    }

    public int RetentionDays
    {
        get => Preferences.Default.Get(RetentionDaysKey, SpamDetector.DefaultRetentionDays);
        set => Preferences.Default.Set(RetentionDaysKey, Math.Max(1, value));
    }
}
