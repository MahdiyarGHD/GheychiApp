using Gheychi.Core.Spam;

namespace Gheychi.App.Services;

public sealed class PreferencesSpamSettings : ISpamSettings
{
    private const string ThresholdKey = "spam_threshold_v1";

    public float Threshold
    {
        get => Preferences.Default.Get(ThresholdKey, SpamDetector.DefaultThreshold);
        set => Preferences.Default.Set(ThresholdKey, Math.Clamp(value, 0f, 1f));
    }
}
