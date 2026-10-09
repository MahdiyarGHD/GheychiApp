using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Rendering.Composition;
using Gheychi.App.Controls.Settings;
using Gheychi.App.Localization;
using Gheychi.App.Platforms.Android;
using Gheychi.App.Ui;
using Gheychi.App.ViewModels;
using Gheychi.Core.Services;
using Gheychi.Core.Spam;
using Gheychi.Core.Updates;

namespace Gheychi.App.Pages;

/// <summary>
/// The overview of the Settings tab. Only this is built with the tab; each screen behind a row is built the first
/// time it is opened, so switching to the tab stays cheap. The tab bar stays visible while a screen is open.
/// </summary>
public partial class SettingsPage : UserControl
{
    private const double SlideDip = 32;
    private static readonly TimeSpan SlideDuration = TimeSpan.FromMilliseconds(160);
    private const string AuthorProfileUrl = "https://github.com/MahdiyarGHD";

    private readonly ISmsService? _sms;
    private readonly ISpamSettings? _spamSettings;
    private readonly ISpamModelUpdater? _models;
    private readonly SpamModelUpdates? _modelUpdates;
    private readonly AppUpdates? _appUpdates;
    private readonly SpamViewModel? _spam;
    private readonly IBlockedSenders? _blocked;
    private readonly Dictionary<SettingsScreenKind, SettingsScreen> _screens = new();
    private readonly Stack<SettingsScreen> _open = new();
    private bool _modelShown;
    private bool _simsShown;
    private int _summaryVersion;
    private int _zIndex;

    public SettingsPage()
    {
        InitializeComponent();

        var services = IPlatformApplication.Current?.Services;
        _sms = services?.GetService<ISmsService>();
        _spamSettings = services?.GetService<ISpamSettings>();
        _models = services?.GetService<ISpamModelUpdater>();
        _modelUpdates = services?.GetService<SpamModelUpdates>();
        if (_modelUpdates is not null)
            _modelUpdates.Changed += (_, _) => MainThread.BeginInvokeOnMainThread(() => _ = ShowModelUpdateAsync());
        _appUpdates = services?.GetService<AppUpdates>();
        if (_appUpdates is not null)
            _appUpdates.Changed += (_, _) => MainThread.BeginInvokeOnMainThread(ShowAboutHint);
        _spam = services?.GetService<SpamViewModel>();
        if (_spam is not null)
            _spam.Changed += () => MainThread.BeginInvokeOnMainThread(UpdateSpamSummary);
        _blocked = services?.GetService<IBlockedSenders>();
        if (_blocked is not null)
            _blocked.Changed += (_, _) => MainThread.BeginInvokeOnMainThread(ShowBlockedHint);

        var loc = LocalizationManager.Instance;
        HeroVersion.Text = $"v{SettingsUi.Digits(AppInfo.Current.VersionString)}";
        ShowAboutHint();
        TileModelValue.Text = loc["Settings_ModelNone"];
        TileCaughtValue.Text = string.Format(loc["Settings_TileCaughtValue"], SettingsUi.Number(0));
    }

    /// <summary>Opens the spam settings on top of whatever is open, for the Spam tab's settings button (the legacy <c>//settings?section=spam</c>).</summary>
    public void OpenSpamSettings()
    {
        while (_open.TryPop(out var screen))
            screen.IsVisible = false;
        Open(SettingsScreenKind.Spam);
    }

    /// <summary>The tab came into view (legacy OnAppearing).</summary>
    public void OnShown()
    {
        UpdateStatus();
        UpdateSpamSummary();
        ShowAboutHint();
        ShowBlockedHint();
        if (!_modelShown)
            _ = ShowModelVersionAsync();
        _ = ShowModelUpdateAsync();
        if (!_simsShown)
            _ = ShowSimsAsync();

        // Back from the default-app prompt or the system settings, what is allowed may have changed.
        MainActivity.Resumed -= OnAppResumed;
        MainActivity.Resumed += OnAppResumed;
    }

    /// <summary>The tab left the screen (legacy OnDisappearing).</summary>
    public void OnHidden()
    {
        MainActivity.Resumed -= OnAppResumed;
    }

    /// <summary>The Settings tab was tapped while shown: every open screen closes, otherwise the page goes to its top.</summary>
    public void OnTabReselected()
    {
        if (_open.Count > 0)
        {
            while (_open.TryPop(out var screen))
                screen.IsVisible = false;
            RefreshRoot();
        }
        else
        {
            ScrollAnimator.ToTop(PageScroll);
        }
    }

    /// <summary>True when it closed an open screen.</summary>
    public bool HandleBack()
    {
        if (_open.Count == 0)
            return false;

        Back();
        return true;
    }

    private void OnAppResumed() => MainThread.BeginInvokeOnMainThread(UpdateStatus);

    // ---- Screens -------------------------------------------------------------------------------

    private SettingsScreen Build(SettingsScreenKind kind)
    {
        SettingsScreen screen = kind switch
        {
            SettingsScreenKind.Spam => new SpamSettingsScreen(),
            SettingsScreenKind.Trusted => new TrustedSendersScreen(),
            SettingsScreenKind.Blocked => new BlockedSendersScreen(),
            SettingsScreenKind.Sims => new SimSettingsScreen(),
            SettingsScreenKind.Notifications => new NotificationSettingsScreen(),
            SettingsScreenKind.Appearance => new AppearanceSettingsScreen(),
            SettingsScreenKind.Privacy => new PrivacySettingsScreen(),
            SettingsScreenKind.About => new AboutSettingsScreen(),
            SettingsScreenKind.Licenses => new LicensesScreen(),
            _ => new SpamAnalyticsScreen()
        };
        screen.IsVisible = false;
        screen.BackRequested += (_, _) => Back();
        screen.OpenRequested += (_, next) => Open(next);
        Root.Children.Add(screen);
        return screen;
    }

    private void Open(SettingsScreenKind kind)
    {
        if (!_screens.TryGetValue(kind, out var screen))
            _screens[kind] = screen = Build(kind);

        _open.Push(screen);
        screen.OnShown();
        // Screens are added in the order they are first built, not opened: the one opened last must be on top.
        screen.ZIndex = ++_zIndex;
        screen.Opacity = 0;
        screen.IsVisible = true;
        _ = SlideInAsync(screen);
    }

    private async Task SlideInAsync(SettingsScreen screen)
    {
        // A screen shown for the first time has no composition visual until the next frame.
        if (ElementComposition.GetElementVisual(screen) is null)
            await OverlayAnimator.SettleAsync();

        // The window is mirrored in Persian, so a positive offset starts on the reading-direction end in both languages.
        await Task.WhenAll(
            OverlayAnimator.FadeAsync(screen, 0, 1, SlideDuration),
            OverlayAnimator.SlideXAsync(screen, SlideDip, 0, SlideDuration, decelerate: true));
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
            RefreshRoot();
        }
    }

    private void RefreshRoot()
    {
        UpdateSpamSummary();
        ShowAboutHint();
        ShowBlockedHint();
        _ = ShowModelVersionAsync();
        _ = ShowModelUpdateAsync();
    }

    private void OnSpamTapped(object? sender, TappedEventArgs e) => Open(SettingsScreenKind.Spam);

    private void OnBlockedTapped(object? sender, TappedEventArgs e) => Open(SettingsScreenKind.Blocked);

    private void OnSimsTapped(object? sender, TappedEventArgs e) => Open(SettingsScreenKind.Sims);

    private void OnNotificationsTapped(object? sender, TappedEventArgs e) => Open(SettingsScreenKind.Notifications);

    private void OnAppearanceTapped(object? sender, TappedEventArgs e) => Open(SettingsScreenKind.Appearance);

    private void OnPrivacyTapped(object? sender, TappedEventArgs e) => Open(SettingsScreenKind.Privacy);

    private void OnAboutTapped(object? sender, TappedEventArgs e) => Open(SettingsScreenKind.About);

    private void OnCaughtTapped(object? sender, TappedEventArgs e) => Open(SettingsScreenKind.Analytics);

    private void OnModelTileTapped(object? sender, TappedEventArgs e) => Open(SettingsScreenKind.Spam);

    private async void OnCreditTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            PulseHeart();
            await Launcher.Default.OpenAsync(new Uri(AuthorProfileUrl));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Opening the author's profile failed: {ex}");
        }
    }

    // On the render thread, like the screen slides: grows to 1.35 in 110 ms, back in 160.
    private void PulseHeart()
    {
        if (ElementComposition.GetElementVisual(CreditHeart) is not { } visual)
            return;

        visual.CenterPoint = new Vector3D(CreditHeart.Bounds.Width / 2, CreditHeart.Bounds.Height / 2, 0);
        var animation = visual.Compositor.CreateVector3DKeyFrameAnimation();
        animation.Target = "Scale";
        animation.InsertKeyFrame(0f, new Vector3D(1, 1, 1));
        animation.InsertKeyFrame(0.41f, new Vector3D(1.35, 1.35, 1), new CubicEaseOut());
        animation.InsertKeyFrame(1f, new Vector3D(1, 1, 1), new CubicEaseIn());
        animation.Duration = TimeSpan.FromMilliseconds(270);
        visual.StartAnimation("Scale", animation);
    }

    // ---- Status --------------------------------------------------------------------------------

    private async void UpdateStatus()
    {
        try
        {
            SetChip(DefaultChip, DefaultLabel, _sms?.IsDefaultSmsApp() ?? true, "Settings_Default", "Settings_NotDefault");
            SetChip(NotificationsChip, NotificationsLabel, AppStatus.NotificationsEnabled(), "Settings_NotificationsOn", "Settings_NotificationsOff");
            SetChip(ContactsChip, ContactsLabel, await AppStatus.ContactsAllowedAsync(), "Settings_ContactsOn", "Settings_ContactsOff");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Reading the app status failed: {ex}");
        }
    }

    private static void SetChip(Border chip, TextBlock label, bool ok, string okKey, string warnKey)
    {
        chip.Classes.Set("warn", !ok);
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

    private void ShowBlockedHint()
    {
        var loc = LocalizationManager.Instance;
        var count = _blocked?.GetAll().Count ?? 0;
        BlockedHint.Text = count == 0
            ? loc["Settings_BlockedNone"]
            : string.Format(loc["Settings_BlockedHint"], SettingsUi.Number(count));
    }

    private void ShowAboutHint()
    {
        var loc = LocalizationManager.Instance;
        var version = SettingsUi.Digits(AppInfo.Current.VersionString);
        AboutHint.Text = string.Format(loc[_appUpdates?.GetAvailable() is null ? "Settings_AboutHint" : "Settings_AboutUpdateHint"], version);
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
            // Off the UI thread: it reads the saved release (JSON) and the model files.
            ModelUpdateDot.IsVisible = _modelUpdates is { } updates && await Task.Run(() => updates.GetAvailableAsync()) is not null;
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
