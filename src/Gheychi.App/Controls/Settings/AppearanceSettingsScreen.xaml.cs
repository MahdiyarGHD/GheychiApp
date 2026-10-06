using Gheychi.App.Localization;
using Gheychi.App.Platforms.Android;
using Gheychi.App.Services;

namespace Gheychi.App.Controls.Settings;

public partial class AppearanceSettingsScreen : SettingsScreen
{
    private readonly (Border Segment, AppTheme Theme)[] _themes;
    private readonly (Border Segment, string Language)[] _languages;

    public AppearanceSettingsScreen()
    {
        InitializeComponent();
        _themes = [(ThemeSystem, AppTheme.Unspecified), (ThemeLight, AppTheme.Light), (ThemeDark, AppTheme.Dark)];
        _languages = [(LanguageSystem, string.Empty), (LanguageEnglish, AppPreferences.LanguageEnglish), (LanguagePersian, AppPreferences.LanguagePersian)];
    }

    public override void OnShown()
    {
        var theme = AppPreferences.Theme;
        SettingsUi.Select(Resources, _themes.Select(t => t.Segment), _themes.FirstOrDefault(t => t.Theme == theme).Segment);
        var language = AppPreferences.Language;
        SettingsUi.Select(Resources, _languages.Select(l => l.Segment), _languages.FirstOrDefault(l => l.Language == language).Segment);
    }

    private void OnThemeTapped(object? sender, TappedEventArgs e)
    {
        var choice = _themes.FirstOrDefault(t => t.Segment == sender);
        if (choice.Segment is null || Application.Current is not { } app)
            return;

        AppPreferences.Theme = choice.Theme;
        app.UserAppTheme = choice.Theme;
        OnShown();
    }

    private async void OnLanguageTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            var choice = _languages.FirstOrDefault(l => l.Segment == sender);
            if (choice.Segment is null || choice.Language == AppPreferences.Language)
                return;

            var loc = LocalizationManager.Instance;
            if (Confirm is null || !await Confirm(loc["Settings_RestartTitle"], loc["Settings_RestartMessage"], loc["Settings_Restart"]))
                return;

            AppPreferences.Language = choice.Language;
            AppStatus.Restart();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Changing the language failed: {ex}");
        }
    }
}
