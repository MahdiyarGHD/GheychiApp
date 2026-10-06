namespace Gheychi.App.Services;

/// <summary>App-wide choices from the Settings tab.</summary>
public static class AppPreferences
{
    private const string DefaultSubIdKey = "default_sim_sub_id_v1";
    private const string LockScreenContentKey = "notification_lock_screen_content_v1";
    private const string ThemeKey = "app_theme_v1";
    private const string LanguageKey = "app_language_v1";

    public const string LanguageEnglish = "en";
    public const string LanguagePersian = "fa";

    /// <summary>The SIM a new message starts on; 0 for the first one.</summary>
    public static int DefaultSubId
    {
        get => Preferences.Default.Get(DefaultSubIdKey, 0);
        set => Preferences.Default.Set(DefaultSubIdKey, value);
    }

    public static bool ShowContentOnLockScreen
    {
        get => Preferences.Default.Get(LockScreenContentKey, true);
        set => Preferences.Default.Set(LockScreenContentKey, value);
    }

    public static AppTheme Theme
    {
        get => (AppTheme)Preferences.Default.Get(ThemeKey, (int)AppTheme.Unspecified);
        set => Preferences.Default.Set(ThemeKey, (int)value);
    }

    /// <summary><see cref="LanguageEnglish"/>, <see cref="LanguagePersian"/>, or empty to follow the phone.</summary>
    public static string Language
    {
        get => Preferences.Default.Get(LanguageKey, string.Empty);
        set => Preferences.Default.Set(LanguageKey, value);
    }
}
