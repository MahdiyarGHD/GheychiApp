using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Gheychi.App.Localization;
using Gheychi.App.Platforms.Android;
using Gheychi.App.Services;
using Gheychi.App.Theming;
using Gheychi.App.Ui;

namespace Gheychi.App.Controls.Settings;

public partial class AppearanceSettingsScreen : SettingsScreen
{
    private readonly (Border Segment, AppTheme Theme)[] _themes;
    private readonly (Border Segment, string Language)[] _languages;
    private readonly List<(AccentScheme Scheme, Border Ring, Icon Check)> _accents = [];

    public AppearanceSettingsScreen()
    {
        InitializeComponent();
        _themes = [(ThemeSystem, AppTheme.Unspecified), (ThemeLight, AppTheme.Light), (ThemeDark, AppTheme.Dark)];
        _languages = [(LanguageSystem, string.Empty), (LanguageEnglish, AppPreferences.LanguageEnglish), (LanguagePersian, AppPreferences.LanguagePersian)];
        BuildAccentPicker();
    }

    public override void OnShown()
    {
        var theme = AppPreferences.Theme;
        SettingsUi.Select(_themes.Select(t => t.Segment), _themes.FirstOrDefault(t => t.Theme == theme).Segment);
        ShowAccent(AccentScheme.Find(AppPreferences.Accent));
        var language = AppPreferences.Language;
        SettingsUi.Select(_languages.Select(l => l.Segment), _languages.FirstOrDefault(l => l.Language == language).Segment);
    }

    private void BuildAccentPicker()
    {
        var loc = LocalizationManager.Instance;
        var check = this.FindResource("Icon.Check") as Geometry;
        var ringColor = Palette.Pick("#1F1F1F", "#E6E6E6");
        var labelColor = Palette.Pick("#595C61", "#9AA0AB");

        foreach (var scheme in AccentScheme.All)
        {
            var tick = new Icon
            {
                Data = check,
                Foreground = Palette.Brush("#FFFFFF"),
                Width = 18,
                Height = 18,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsVisible = false
            };
            var ring = new Border
            {
                Width = 46,
                Height = 46,
                CornerRadius = new CornerRadius(23),
                BorderThickness = new Thickness(2.5),
                BorderBrush = Palette.Transparent,
                Tag = ringColor,
                Child = new Border
                {
                    Margin = new Thickness(3),
                    CornerRadius = new CornerRadius(20),
                    Background = Palette.Brush(scheme.Swatch),
                    Child = tick
                }
            };
            var name = loc[scheme.NameKey];
            var item = new StackPanel
            {
                Spacing = 6,
                Margin = new Thickness(0, 0, 0, 8),
                Background = Palette.Transparent,
                Tag = scheme,
                Children =
                {
                    ring,
                    new TextBlock
                    {
                        Text = name,
                        FontSize = 11.5,
                        Foreground = labelColor,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    }
                }
            };
            AutomationProperties.SetName(item, name);
            item.Tapped += OnAccentTapped;
            AccentGrid.Children.Add(item);
            _accents.Add((scheme, ring, tick));
        }
    }

    private void ShowAccent(AccentScheme selected)
    {
        foreach (var (scheme, ring, check) in _accents)
        {
            var on = scheme == selected;
            ring.BorderBrush = on ? (IBrush)ring.Tag! : Palette.Transparent;
            check.IsVisible = on;
        }
    }

    private async void OnAccentTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if ((sender as Control)?.Tag is not AccentScheme scheme || scheme == AccentScheme.Find(AppPreferences.Accent))
                return;

            var loc = LocalizationManager.Instance;
            if (!await Confirm(loc["Settings_RestartTitle"], loc["Settings_RestartAccentMessage"], loc["Settings_Restart"]))
                return;

            AppPreferences.Accent = scheme.Id;
            AppStatus.Restart();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Changing the accent colour failed: {ex}");
        }
    }

    // Many colors are picked when a screen is built, so a theme is applied by restarting rather than live.
    private async void OnThemeTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            var choice = _themes.FirstOrDefault(t => t.Segment == sender);
            if (choice.Segment is null || choice.Theme == AppPreferences.Theme)
                return;

            var loc = LocalizationManager.Instance;
            if (!await Confirm(loc["Settings_RestartTitle"], loc["Settings_RestartThemeMessage"], loc["Settings_Restart"]))
                return;

            AppPreferences.Theme = choice.Theme;
            AppStatus.Restart();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Changing the theme failed: {ex}");
        }
    }

    private async void OnLanguageTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            var choice = _languages.FirstOrDefault(l => l.Segment == sender);
            if (choice.Segment is null || choice.Language == AppPreferences.Language)
                return;

            var loc = LocalizationManager.Instance;
            if (!await Confirm(loc["Settings_RestartTitle"], loc["Settings_RestartMessage"], loc["Settings_Restart"]))
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
