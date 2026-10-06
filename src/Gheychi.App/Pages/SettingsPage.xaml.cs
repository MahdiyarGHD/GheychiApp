using System.Globalization;
using Gheychi.App.Controls;
using Gheychi.App.Localization;
using Gheychi.App.Platforms.Android.Notifications;
using Gheychi.App.ViewModels;
using Gheychi.Core.Models;
using Gheychi.Core.Services;
using Gheychi.Core.Spam;

namespace Gheychi.App.Pages;

public partial class SettingsPage : ContentPage, IQueryAttributable
{
    private const int MinThresholdPercent = 50;
    private const int MaxThresholdPercent = 99;
    private static readonly int[] RetentionChoices = [7, 14, 30, 90];

    private static readonly Color SelectedLight = Color.FromArgb("#2E6B4C");
    private static readonly Color SelectedDark = Color.FromArgb("#8FE0BE");
    private static readonly Color SelectedTextDark = Color.FromArgb("#0A1A11");
    private static readonly Color IdleLight = Color.FromArgb("#E3EAE2");
    private static readonly Color IdleDark = Color.FromArgb("#1D3026");
    private static readonly Color IdleTextLight = Color.FromArgb("#1B1E24");
    private static readonly Color IdleTextDark = Color.FromArgb("#E8EAED");
    private static readonly Color IdleHintLight = Color.FromArgb("#59615A");
    private static readonly Color IdleHintDark = Color.FromArgb("#9AA0AB");

    private readonly ISmsService? _sms;
    private readonly ISpamSettings? _spamSettings;
    private readonly ISpamModelUpdater? _models;
    private readonly SpamViewModel? _spam;
    private readonly (Border Segment, int Percent)[] _presets;
    private readonly Stack<View> _screens = new();
    private IReadOnlyList<SimCardInfo> _sims = [];
    private int? _modelVersion;
    private bool _syncingSlider;
    private Window? _window;

    public SettingsPage()
    {
        InitializeComponent();

        var services = IPlatformApplication.Current?.Services;
        _sms = services?.GetService<ISmsService>();
        _spamSettings = services?.GetService<ISpamSettings>();
        _models = services?.GetService<ISpamModelUpdater>();
        _spam = services?.GetService<SpamViewModel>();
        if (_spam is not null)
            _spam.Changed += UpdateSpam;

        _presets = [(PresetRelaxed, 95), (PresetBalanced, 85), (PresetStrict, 70)];
        foreach (var (segment, percent) in _presets)
            PresetHint(segment).Text = SpamItem.FormatPercent(percent);

        PaintSegments([ThemeSystem, ThemeLight, ThemeDark], ThemeSystem);
        PaintSegments([LanguageSystem, LanguageEnglish, LanguageFarsi], LanguageSystem);

        var version = AppInfo.Current.VersionString;
        HeroVersion.Text = $"v{Digits(version)}";
        AboutVersion.Text = Digits($"{version} ({AppInfo.Current.BuildString})");
        AboutHint.Text = string.Format(LocalizationManager.Instance["Settings_AboutHint"], Digits(version));

        // The chevrons point along the reading direction.
        if (CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft && Resources["Chevron"] is Style chevron)
        {
            foreach (var image in this.GetVisualTreeDescendants().OfType<Image>().Where(i => i.Style == chevron))
                image.Rotation = 180;
        }

        UpdateSpam();
        UpdateModel();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("section", out var section) && section as string == "spam")
        {
            _screens.Clear();
            HideScreens();
            BindSensitivity();
            Open(SpamScreen);
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        UpdateDefaultApp();
        _ = LoadAsync();

        // Back from the default-app prompt or the system settings, the state may have changed.
        if (_window is null && Window is { } window)
        {
            _window = window;
            window.Activated += OnWindowActivated;
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (_window is not null)
        {
            _window.Activated -= OnWindowActivated;
            _window = null;
        }
    }

    protected override bool OnBackButtonPressed()
    {
        if (Overlay.HandleBack())
            return true;

        if (_screens.Count == 0)
            return base.OnBackButtonPressed();

        Back();
        return true;
    }

    private void OnWindowActivated(object? sender, EventArgs e) => UpdateDefaultApp();

    private async Task LoadAsync()
    {
        try
        {
            if (_spam is not null)
                await _spam.EnsureLoadedAsync();
            if (_models is not null)
                _modelVersion = await _models.GetActiveVersionAsync();
            UpdateModel();

            if (_sms is not null && await _sms.IsDualSimAsync())
                _sims = await _sms.GetActiveSimsAsync();
            UpdateSims();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Loading settings failed: {ex}");
        }
    }

    // ---- Screens -------------------------------------------------------------------------------

    private void Open(View screen)
    {
        _screens.Push(screen);
        screen.IsVisible = true;
        screen.Opacity = 0;
        screen.TranslationX = CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft ? -32 : 32;
        _ = Task.WhenAll(screen.FadeToAsync(1, 160), screen.TranslateToAsync(0, 0, 160, Easing.CubicOut));
    }

    private void Back()
    {
        if (_screens.TryPop(out var screen))
            screen.IsVisible = false;
    }

    private void HideScreens()
    {
        foreach (var screen in new View[] { SpamScreen, TrustedScreen, SimsScreen, NotificationsScreen, AppearanceScreen, PrivacyScreen, AboutScreen })
            screen.IsVisible = false;
    }

    private void OnSubPageBackTapped(object? sender, TappedEventArgs e) => Back();

    private void OnSpamTapped(object? sender, TappedEventArgs e)
    {
        BindSensitivity();
        Open(SpamScreen);
    }

    private void OnSimsTapped(object? sender, TappedEventArgs e) => Open(SimsScreen);

    private void OnNotificationsTapped(object? sender, TappedEventArgs e) => Open(NotificationsScreen);

    private void OnAppearanceTapped(object? sender, TappedEventArgs e) => Open(AppearanceScreen);

    private void OnPrivacyTapped(object? sender, TappedEventArgs e) => Open(PrivacyScreen);

    private void OnAboutTapped(object? sender, TappedEventArgs e) => Open(AboutScreen);

    // TODO: wire each row that lands here: spam on/off, default SIM, lock-screen content, theme, language, model updates, licenses.
    private void OnComingSoonTapped(object? sender, TappedEventArgs e) =>
        Toast.Show(LocalizationManager.Instance["Settings_ComingSoon"]);

    // ---- Overview ------------------------------------------------------------------------------

    private void UpdateDefaultApp()
    {
        var loc = LocalizationManager.Instance;
        var isDefault = IsDefaultSmsApp();
        var dark = IsDark;
        DefaultLabel.Text = loc[isDefault ? "Settings_Default" : "Settings_NotDefault"];
        DefaultDot.Color = Color.FromArgb(isDefault ? "#2E9B63" : "#C7841A");
        DefaultChip.BackgroundColor = Color.FromArgb(isDefault
            ? dark ? "#1B3A2B" : "#DDF0E4"
            : dark ? "#3A3020" : "#FCF2E1");
        DefaultLabel.TextColor = Color.FromArgb(isDefault
            ? dark ? "#8FE0BE" : "#1B5E43"
            : dark ? "#E8C27A" : "#8A5A0E");
    }

    private static bool IsDefaultSmsApp()
    {
#if ANDROID
        var context = Platform.AppContext;
        return global::Android.Provider.Telephony.Sms.GetDefaultSmsPackage(context) == context.PackageName;
#else
        return true;
#endif
    }

    private void OnDefaultChipTapped(object? sender, TappedEventArgs e)
    {
        if (!IsDefaultSmsApp())
            _ = _sms?.EnsureDefaultSmsAppAsync();
    }

    private void UpdateModel()
    {
        var loc = LocalizationManager.Instance;
        var version = _modelVersion is { } v ? Digits(v.ToString(CultureInfo.InvariantCulture)) : null;
        TileModelValue.Text = version is null ? loc["Settings_ModelNone"] : string.Format(loc["Settings_TileModelValue"], version);
        ModelVersion.Text = version is null ? loc["Settings_ModelNone"] : string.Format(loc["Spam_InfoModelValue"], version);
        AboutModel.Text = version is null ? loc["Settings_ModelNone"] : string.Format(loc["Spam_InfoModelValue"], version);
    }

    // ---- Spam ----------------------------------------------------------------------------------

    private int ThresholdPercent =>
        Math.Clamp((int)MathF.Round((_spamSettings?.Threshold ?? SpamDetector.DefaultThreshold) * 100f), MinThresholdPercent, MaxThresholdPercent);

    private int RetentionDays => _spam?.RetentionDays ?? _spamSettings?.RetentionDays ?? SpamDetector.DefaultRetentionDays;

    private void UpdateSpam()
    {
        var loc = LocalizationManager.Instance;
        SpamHint.Text = string.Format(loc["Settings_SpamHint"], SpamItem.FormatPercent(ThresholdPercent), FormatDays(RetentionDays));
        AutoClearValue.Text = FormatDays(RetentionDays);

        var trusted = _spam?.TrustedSenders().Count ?? 0;
        TrustedHint.Text = trusted == 0 ? loc["Settings_TrustedNone"] : string.Format(loc["Settings_TrustedHint"], Count(trusted));

        var count = _spam?.Count ?? 0;
        ClearSpamHint.Text = count == 0 ? loc["Settings_ClearSpamNone"] : string.Format(loc["Settings_ClearSpamHint"], Count(count));
        TileCaughtValue.Text = string.Format(loc["Settings_TileCaughtValue"], Count(_spam?.CountSince(DateTime.Now.AddDays(-7)) ?? 0));
    }

    private void BindSensitivity()
    {
        var percent = ThresholdPercent;
        _syncingSlider = true;
        SensitivitySlider.Value = percent;
        _syncingSlider = false;
        ShowSensitivity(percent);
    }

    private void ShowSensitivity(int percent)
    {
        var text = SpamItem.FormatPercent(percent);
        SensitivityValue.Text = text;
        SensitivityExplain.Text = string.Format(LocalizationManager.Instance["Settings_SensitivityExplain"], text);
        foreach (var (segment, preset) in _presets)
            PaintSegment(segment, preset == percent);
    }

    private void SaveThreshold(int percent)
    {
        if (_spamSettings is null || percent == ThresholdPercent)
            return;

        _spamSettings.Threshold = percent / 100f;
        UpdateSpam();
    }

    private void OnSensitivityChanged(object? sender, ValueChangedEventArgs e)
    {
        if (_syncingSlider)
            return;

        var percent = (int)Math.Round(e.NewValue);
        ShowSensitivity(percent);
        SaveThreshold(percent);
    }

    private void OnPresetTapped(object? sender, TappedEventArgs e)
    {
        var preset = _presets.FirstOrDefault(p => p.Segment == sender);
        if (preset.Segment is null)
            return;

        SaveThreshold(preset.Percent);
        BindSensitivity();
    }

    private async void OnAutoClearTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            var loc = LocalizationManager.Instance;
            var labels = RetentionChoices.Select(FormatDays).ToArray();
            var choice = await Shell.Current.DisplayActionSheetAsync(loc["Settings_AutoClearTitle"], loc["Chat_Cancel"], null, labels);
            var index = Array.IndexOf(labels, choice);
            if (index < 0 || RetentionChoices[index] == RetentionDays)
                return;

            if (_spam is not null)
                await _spam.SetRetentionDaysAsync(RetentionChoices[index]);
            else if (_spamSettings is not null)
                _spamSettings.RetentionDays = RetentionChoices[index];
            UpdateSpam();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Changing spam auto-clear failed: {ex}");
        }
    }

    private void OnTrustedTapped(object? sender, TappedEventArgs e)
    {
        BindTrusted();
        Open(TrustedScreen);
    }

    private void BindTrusted() =>
        TrustedList.ItemsSource = (_spam?.TrustedSenders() ?? [])
            .Select(key => new TrustedSenderRow(key, PhoneNumberNormalizer.FormatDisplay(key)))
            .OrderBy(row => row.Display, StringComparer.CurrentCulture)
            .ToList();

    private void OnTrustedRemoveTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Element)?.BindingContext is not TrustedSenderRow row || _spam is null)
            return;

        _spam.SetTrusted(row.Key, false);
        BindTrusted();
        UpdateSpam();
    }

    // ---- SIM -----------------------------------------------------------------------------------

    private void UpdateSims()
    {
        var noName = LocalizationManager.Instance["Settings_SimNoName"];
        var rows = _sims
            .OrderBy(s => s.SlotIndex)
            .Select(s => new SimSettingsRow($"SIM {Digits(s.SlotIndex.ToString(CultureInfo.InvariantCulture))}", string.IsNullOrWhiteSpace(s.DisplayName) ? noName : s.DisplayName))
            .ToList();

        SimsRow.IsVisible = rows.Count > 1;
        SimsHint.Text = string.Join(" · ", rows.Select(r => r.Subtitle == noName ? r.Title : $"{r.Title} {r.Subtitle}"));
        BindableLayout.SetItemsSource(SimList, rows);
    }

    // ---- Notifications -------------------------------------------------------------------------

    private void OnSystemNotificationsTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            NotificationChannels.OpenAppSettings(Platform.AppContext);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Opening notification settings failed: {ex}");
        }
    }

    // ---- Privacy -------------------------------------------------------------------------------

    private void OnClearSearchesTapped(object? sender, TappedEventArgs e)
    {
        SearchViewModel.ClearSavedRecentSearches();
        Toast.Show(LocalizationManager.Instance["Settings_ClearSearchesDone"]);
    }

    private async void OnClearSpamTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            var loc = LocalizationManager.Instance;
            if (_spam is { HasAny: true } && await Overlay.ConfirmAsync(loc["Spam_ClearAllTitle"], loc["Spam_ClearAllMessage"], loc["Spam_ClearAll"]))
                await _spam.ClearAllAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Clearing spam from settings failed: {ex}");
        }
    }

    // ---- Helpers -------------------------------------------------------------------------------

    private static bool IsDark => Application.Current?.RequestedTheme == AppTheme.Dark;

    private static bool IsPersian => CultureInfo.CurrentUICulture.Name.StartsWith("fa", StringComparison.OrdinalIgnoreCase);

    private static string Digits(string text) => IsPersian ? DateFormattingService.ToPersianDigits(text) : text;

    private static string Count(int value) => Digits(value.ToString(CultureInfo.InvariantCulture));

    private static string FormatDays(int days) => string.Format(LocalizationManager.Instance["Settings_Days"], Count(days));

    private static Label PresetHint(Border segment) => (Label)((VerticalStackLayout)segment.Content!).Children[1];

    private static void PaintSegments(Border[] segments, Border selected)
    {
        foreach (var segment in segments)
            PaintSegment(segment, segment == selected);
    }

    private static void PaintSegment(Border segment, bool selected)
    {
        var dark = IsDark;
        segment.BackgroundColor = selected ? dark ? SelectedDark : SelectedLight : dark ? IdleDark : IdleLight;
        var labels = segment.Content is Layout layout ? layout.Children.OfType<Label>().ToList() : [(Label)segment.Content!];
        for (var i = 0; i < labels.Count; i++)
        {
            labels[i].TextColor = selected
                ? dark ? SelectedTextDark : Colors.White
                : i == 0 ? dark ? IdleTextDark : IdleTextLight : dark ? IdleHintDark : IdleHintLight;
        }
    }
}
