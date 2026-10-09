namespace Gheychi.App.Services;

/// <summary>App-wide choices from the Settings tab.</summary>
public static class AppPreferences
{
    private const string DefaultSubIdKey = "default_sim_sub_id_v1";
    private const string LockScreenContentKey = "notification_lock_screen_content_v1";
    private const string ThemeKey = "app_theme_v1";
    private const string LanguageKey = "app_language_v1";
    private const string AccentKey = "app_accent_v1";
    private const string TextSizeKey = "app_text_size_v1";

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
        get => (AppTheme)Committed.GetInt(ThemeKey, (int)AppTheme.Unspecified);
        set => Committed.Edit()!.PutInt(ThemeKey, (int)value)!.Commit();
    }

    /// <summary>The <see cref="Theming.AccentScheme.Id"/> picked in Appearance; empty for the default.</summary>
    public static string Accent
    {
        get => Committed.GetString(AccentKey, string.Empty) ?? string.Empty;
        set => Committed.Edit()!.PutString(AccentKey, value)!.Commit();
    }

    /// <summary>The index into <see cref="Theming.TextScale.Steps"/>; chosen in Appearance.</summary>
    public static int TextSize
    {
        get => Committed.GetInt(TextSizeKey, Theming.TextScale.DefaultStep);
        set => Committed.Edit()!.PutInt(TextSizeKey, value)!.Commit();
    }

    /// <summary><see cref="LanguageEnglish"/>, <see cref="LanguagePersian"/>, or empty to follow the phone.</summary>
    public static string Language
    {
        get => Committed.GetString(LanguageKey, string.Empty) ?? string.Empty;
        set => Committed.Edit()!.PutString(LanguageKey, value)!.Commit();
    }

    // Theme, accent and language are applied by restarting the app, and Preferences writes to disk in the background:
    // the restart would end the process before the choice is saved. These are written synchronously instead.
    private static Android.Content.ISharedPreferences Committed =>
        Android.App.Application.Context.GetSharedPreferences("gheychi_restart_settings", Android.Content.FileCreationMode.Private)!;
}
