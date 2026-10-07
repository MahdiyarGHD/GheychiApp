using Gheychi.App.Controls.Settings;
using Gheychi.App.Localization;
using Gheychi.App.Platforms.Android;
using Gheychi.App.ViewModels;
using Gheychi.Core.Services;
using Gheychi.Core.Spam;

namespace Gheychi.App.Pages;

/// <summary>
/// The overview of the Settings tab. Only this is built with the tab; each screen behind a row is built the first
/// time it is opened, so switching to the tab stays cheap.
/// </summary>
public partial class SettingsPage : ContentPage, IQueryAttributable
{
    private const uint SlideMs = 160;

    private readonly ISmsService? _sms;
    private readonly ISpamSettings? _spamSettings;
    private readonly ISpamModelUpdater? _models;
    private readonly SpamModelUpdates? _modelUpdates;
    private readonly SpamViewModel? _spam;
    private readonly Dictionary<SettingsScreenKind, SettingsScreen> _screens = new();
    private readonly Stack<SettingsScreen> _open = new();
    private bool _modelShown;
    private bool _simsShown;
    private int _summaryVersion;
    private Window? _window;

    public SettingsPage()
    {
        InitializeComponent();
        TabPageInsets.Attach(this);
        SettingsUi.MirrorChevrons(this, Resources);

        var services = IPlatformApplication.Current?.Services;
        _sms = services?.GetService<ISmsService>();
        _spamSettings = services?.GetService<ISpamSettings>();
        _models = services?.GetService<ISpamModelUpdater>();
        _modelUpdates = services?.GetService<SpamModelUpdates>();
        if (_modelUpdates is not null)
            _modelUpdates.Changed += (_, _) => MainThread.BeginInvokeOnMainThread(() => _ = ShowModelUpdateAsync());
        _spam = services?.GetService<SpamViewModel>();
        if (_spam is not null)
            _spam.Changed += UpdateSpamSummary;

        var loc = LocalizationManager.Instance;
        var version = SettingsUi.Digits(AppInfo.Current.VersionString);
        HeroVersion.Text = $"v{version}";
        AboutHint.Text = string.Format(loc["Settings_AboutHint"], version);
        TileModelValue.Text = loc["Settings_ModelNone"];
        TileCaughtValue.Text = string.Format(loc["Settings_TileCaughtValue"], SettingsUi.Number(0));
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue("section", out var section) || section as string != "spam")
            return;

        while (_open.TryPop(out var screen))
            screen.IsVisible = false;
        Open(SettingsScreenKind.Spam);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        UpdateStatus();
        UpdateSpamSummary();
        if (!_modelShown)
            _ = ShowModelVersionAsync();
        _ = ShowModelUpdateAsync();
        if (!_simsShown)
            _ = ShowSimsAsync();

        // Back from the default-app prompt or the system settings, what is allowed may have changed.
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

        if (_open.Count == 0)
            return base.OnBackButtonPressed();

        Back();
        return true;
    }

    private void OnWindowActivated(object? sender, EventArgs e) => UpdateStatus();

    // ---- Screens -------------------------------------------------------------------------------

    private SettingsScreen Build(SettingsScreenKind kind)
    {
        SettingsScreen screen = kind switch
        {
            SettingsScreenKind.Spam => new SpamSettingsScreen(),
            SettingsScreenKind.Trusted => new TrustedSendersScreen(),
            SettingsScreenKind.Sims => new SimSettingsScreen(),
            SettingsScreenKind.Notifications => new NotificationSettingsScreen(),
            SettingsScreenKind.Appearance => new AppearanceSettingsScreen(),
            SettingsScreenKind.Privacy => new PrivacySettingsScreen(),
            SettingsScreenKind.About => new AboutSettingsScreen(),
            SettingsScreenKind.Licenses => new LicensesScreen(),
            _ => new SpamAnalyticsScreen()
        };
        screen.IsVisible = false;
        screen.Confirm = Overlay.ConfirmAsync;
        screen.BackRequested += (_, _) => Back();
        screen.OpenRequested += (_, next) => Open(next);
        Root.Children.Insert(Root.Children.IndexOf(Overlay), screen);
        return screen;
    }

    private void Open(SettingsScreenKind kind)
    {
        if (!_screens.TryGetValue(kind, out var screen))
            _screens[kind] = screen = Build(kind);

        _open.Push(screen);
        screen.OnShown();
        screen.IsVisible = true;
        screen.Opacity = 0;
        screen.TranslationX = SettingsUi.IsRightToLeft ? -32 : 32;
        _ = Task.WhenAll(screen.FadeToAsync(1, SlideMs), screen.TranslateToAsync(0, 0, SlideMs, Easing.CubicOut));
    }

    private void Back()
    {
        if (!_open.TryPop(out var screen))
            return;

        screen.IsVisible = false;
        if (_open.TryPeek(out var below))
        {
            below.OnShown();
        }
        else
        {
            UpdateSpamSummary();
            _ = ShowModelVersionAsync();
            _ = ShowModelUpdateAsync();
        }
    }

    private void OnSpamTapped(object? sender, TappedEventArgs e) => Open(SettingsScreenKind.Spam);

    private void OnSimsTapped(object? sender, TappedEventArgs e) => Open(SettingsScreenKind.Sims);

    private void OnNotificationsTapped(object? sender, TappedEventArgs e) => Open(SettingsScreenKind.Notifications);

    private void OnAppearanceTapped(object? sender, TappedEventArgs e) => Open(SettingsScreenKind.Appearance);

    private void OnPrivacyTapped(object? sender, TappedEventArgs e) => Open(SettingsScreenKind.Privacy);

    private void OnAboutTapped(object? sender, TappedEventArgs e) => Open(SettingsScreenKind.About);

    private void OnCaughtTapped(object? sender, TappedEventArgs e) => Open(SettingsScreenKind.Analytics);

    private void OnModelTileTapped(object? sender, TappedEventArgs e) => Open(SettingsScreenKind.Spam);

    // ---- Status --------------------------------------------------------------------------------

    private async void UpdateStatus()
    {
        try
        {
            SetChip(DefaultChip, DefaultDot, DefaultLabel, _sms?.IsDefaultSmsApp() ?? true, "Settings_Default", "Settings_NotDefault");
            SetChip(NotificationsChip, NotificationsDot, NotificationsLabel, AppStatus.NotificationsEnabled(), "Settings_NotificationsOn", "Settings_NotificationsOff");
            SetChip(ContactsChip, ContactsDot, ContactsLabel, await AppStatus.ContactsAllowedAsync(), "Settings_ContactsOn", "Settings_ContactsOff");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Reading the app status failed: {ex}");
        }
    }

    private void SetChip(Border chip, BoxView dot, Label label, bool ok, string okKey, string warnKey)
    {
        chip.Style = (Style)Resources[ok ? "ChipOk" : "ChipWarn"];
        dot.Style = (Style)Resources[ok ? "ChipDotOk" : "ChipDotWarn"];
        label.Style = (Style)Resources[ok ? "ChipLabelOk" : "ChipLabelWarn"];
        label.Text = LocalizationManager.Instance[ok ? okKey : warnKey];
    }

    private void OnDefaultChipTapped(object? sender, TappedEventArgs e)
    {
        if (_sms is { } sms && !sms.IsDefaultSmsApp())
            _ = sms.EnsureDefaultSmsAppAsync();
    }

    private async void OnNotificationsChipTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (AppStatus.NotificationsEnabled())
                return;

            await AppStatus.RequestNotificationsAsync();
            UpdateStatus();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Asking for notifications failed: {ex}");
        }
    }

    private async void OnContactsChipTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (await AppStatus.ContactsAllowedAsync())
                return;

            await AppStatus.RequestContactsAsync();
            UpdateStatus();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Asking for contacts failed: {ex}");
        }
    }

    // ---- Summaries -----------------------------------------------------------------------------

    private async void UpdateSpamSummary()
    {
        var loc = LocalizationManager.Instance;
        var settings = _spamSettings;
        SpamHint.Text = settings is { Enabled: false }
            ? loc["Settings_SpamOffHint"]
            : string.Format(loc["Settings_SpamHint"],
                SpamItem.FormatPercent((int)MathF.Round((settings?.Threshold ?? SpamDetector.DefaultThreshold) * 100f)),
                SettingsUi.Days(settings?.RetentionDays ?? SpamDetector.DefaultRetentionDays));

        if (_spam is null)
            return;

        var version = ++_summaryVersion;
        try
        {
            var analytics = await _spam.GetAnalyticsAsync();
            if (version == _summaryVersion)
                TileCaughtValue.Text = string.Format(loc["Settings_TileCaughtValue"], SettingsUi.Number(analytics.Last7Days));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Reading spam analytics failed: {ex}");
        }
    }

    private async Task ShowModelVersionAsync()
    {
        try
        {
            if (_models is not null && await _models.GetActiveVersionAsync() is { } version)
            {
                TileModelValue.Text = string.Format(LocalizationManager.Instance["Spam_InfoModelValue"], SettingsUi.Number(version));
                _modelShown = true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Reading the spam model version failed: {ex}");
        }
    }

    private async Task ShowModelUpdateAsync()
    {
        try
        {
            ModelUpdateDot.IsVisible = _modelUpdates is not null && await _modelUpdates.GetAvailableAsync() is not null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Reading the spam model update failed: {ex}");
        }
    }

    private async Task ShowSimsAsync()
    {
        try
        {
            if (_sms is null || !await _sms.IsDualSimAsync())
                return;

            var sims = (await _sms.GetActiveSimsAsync()).OrderBy(s => s.SlotIndex).ToList();
            SimsRow.IsVisible = sims.Count > 1;
            SimsHint.Text = string.Join(" · ", sims.Select(s =>
                string.IsNullOrWhiteSpace(s.DisplayName) ? $"SIM {SettingsUi.Number(s.SlotIndex)}" : s.DisplayName));
            _simsShown = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Reading SIM cards for settings failed: {ex}");
        }
    }
}
