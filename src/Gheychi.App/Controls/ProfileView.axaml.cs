using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Gheychi.App.Localization;
using Gheychi.App.Platforms.Android;
using Gheychi.App.Platforms.Android.Notifications;
using Gheychi.App.Ui;
using Gheychi.App.ViewModels;
using Gheychi.Core.Models;
using Gheychi.Core.Notifications;
using Gheychi.Core.Services;

namespace Gheychi.App.Controls;

/// <summary>The conversation's profile page: who it is with, what can be done with them, and per-conversation settings.</summary>
public partial class ProfileView : UserControl
{
    private static readonly IBrush SwitchOnTrack = Palette.Brush("#2E7D5B");
    private static readonly IBrush SwitchOffTrack = Palette.Pick("#C6C8CB", "#46484A");
    private static readonly IBrush SwitchOnThumb = Palette.Brush("#FFFFFF");
    private static readonly IBrush SwitchOffThumb = Palette.Pick("#FFFFFF", "#9AA0AB");
    private static readonly IBrush RowTitleBrush = Palette.Pick("#1B1E24", "#E8EAED");
    private static readonly IBrush BlockTitleBrush = Palette.Pick("#B3261E", "#F2B8B5");

    private readonly ISmsService? _sms;
    private readonly IThreadSettings? _settings;
    private readonly IDateFormattingService _dates;
    private readonly SpamViewModel? _spam;
    private readonly IBlockedSenders? _blocked;
    private IReadOnlyList<SpamItem> _spamItems = [];

    private ThreadItem _thread = ThreadItem.Empty;
    private ThreadProfileActions _actions = ThreadProfileActions.For(null);
    private string _sendAddress = string.Empty;
    private bool _isArchived;
    private int _bindVersion;
    private IReadOnlyList<SimCardInfo> _sims = [];
    private IReadOnlyDictionary<int, int> _slotBySubId = new Dictionary<int, int>();

    public ProfileView()
    {
        InitializeComponent();

        var services = IPlatformApplication.Current?.Services;
        _sms = services?.GetService<ISmsService>();
        _settings = services?.GetService<IThreadSettings>();
        _dates = services?.GetService<IDateFormattingService>() ?? new DateFormattingService();
        _spam = services?.GetService<SpamViewModel>();
        if (_spam is not null)
            _spam.Changed += UpdateSpam;
        _blocked = services?.GetService<IBlockedSenders>();

        SetSwitch(SnoozeSwitch, SnoozeThumb, on: false);
        SetSwitch(SpamImmuneSwitch, SpamImmuneThumb, on: false);
    }

    public event EventHandler? BackRequested;

    /// <summary>Go back to the chat and start typing a message.</summary>
    public event EventHandler? TextRequested;

    /// <summary>Open the chat's search.</summary>
    public event EventHandler? SearchRequested;

    /// <summary>The conversation should be archived (true) or brought back to the inbox (false).</summary>
    public event EventHandler<bool>? ArchiveRequested;

    /// <summary>The ••• of a spam message in the list was tapped; the host shows the spam menu for it.</summary>
    public event EventHandler<SpamItem>? SpamMenuRequested;

    /// <summary>Shows <paramref name="thread"/>; call before the page slides in.</summary>
    public void Bind(ThreadItem thread, bool isArchived)
    {
        var version = ++_bindVersion;
        _thread = thread;
        _isArchived = isArchived;
        _sendAddress = PhoneNumberNormalizer.ToSendAddress(thread.Phone);
        _actions = ThreadProfileActions.For(_sendAddress);

        ProfilePage.IsVisible = true;
        LinksPage.IsVisible = false;
        SpamListPage.IsVisible = false;

        BindIdentity();
        ApplyActions();
        BindNumber();
        BindArchive();
        BindBlocked();
        UpdateSnooze();
        UpdateNotificationState();
        UpdateSpam();
        if (_spam is not null)
            _ = _spam.EnsureLoadedAsync();

        SimPill.IsVisible = SimRow.IsVisible = false;
        LinksHint.Text = LocalizationManager.Instance["Profile_LinksReading"];
        _links = [];

        _ = LoadSimsAsync(version);

        // Back from the system's notification settings the "Custom" tag may have changed.
        MainActivity.Resumed -= OnAppResumed;
        MainActivity.Resumed += OnAppResumed;
    }

    /// <summary>Called once the page has slid in.</summary>
    public void OnOpened() => _ = LoadLinksAsync(_bindVersion);

    /// <summary>Called once the page is out of sight.</summary>
    public void Reset()
    {
        _bindVersion++;
        _links = [];
        LinksList.ItemsSource = null;
        _spamItems = [];
        SpamList.ItemsSource = null;
        MainActivity.Resumed -= OnAppResumed;
    }

    // Returns true when it consumed the back press.
    public bool HandleBack()
    {
        if (SpamListPage.IsVisible)
        {
            SpamListPage.IsVisible = false;
            return true;
        }

        if (!LinksPage.IsVisible)
            return false;

        LinksPage.IsVisible = false;
        return true;
    }

    private void OnAppResumed() => MainThread.BeginInvokeOnMainThread(UpdateNotificationState);

    private static void SetSwitch(Border track, Border thumb, bool on, bool enabled = true)
    {
        track.Background = on ? SwitchOnTrack : SwitchOffTrack;
        thumb.Background = on ? SwitchOnThumb : SwitchOffThumb;
        thumb.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        track.Opacity = enabled ? 1 : 0.5;
    }

    // ---- Identity ------------------------------------------------------------------------------

    private void BindIdentity()
    {
        NameLabel.Text = _thread.Name;
        PhoneLabel.Text = _thread.Phone;
        PhoneLabel.IsVisible = !string.Equals(_thread.Name, _thread.Phone, StringComparison.Ordinal);

        AvatarIcon.Data = IconCatalog.Find(_thread.IconFile);
        AvatarIcon.IsVisible = _thread.HasIcon;
        AvatarInitials.Text = _thread.Initials;
        AvatarInitials.IsVisible = _thread.HasNoIcon;
    }

    // Each button is shown only when the conversation can do it, and the ones left share the row.
    private void ApplyActions()
    {
        var buttons = new (Border Button, bool Visible)[]
        {
            (CallButton, _actions.Call),
            (TextButton, _actions.Text),
            (DetailsButton, _actions.Details),
            (SearchButton, _actions.Search)
        };

        ActionsGrid.ColumnDefinitions.Clear();
        var column = 0;
        foreach (var (button, visible) in buttons)
        {
            button.IsVisible = visible;
            if (!visible)
                continue;

            ActionsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            Grid.SetColumn(button, column++);
        }
    }

    private void BindNumber()
    {
        var loc = LocalizationManager.Instance;
        NumberLabel.Text = _thread.Phone;
        NumberKindLabel.Text = _actions.Call ? loc["Profile_NumberMobile"] : loc["Profile_NumberSender"];
        NumberCallButton.IsVisible = _actions.Call;
    }

    // ---- SIM -----------------------------------------------------------------------------------

    private async Task LoadSimsAsync(int version)
    {
        if (_sms is null)
            return;

        try
        {
            var dual = await _sms.IsDualSimAsync();
            var sims = await _sms.GetActiveSimsAsync();
            var slots = await _sms.GetSimSlotMapAsync();
            if (version != _bindVersion || !dual)
                return;

            _sims = sims;
            _slotBySubId = slots;
            UpdateSim();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Reading SIM info for the profile failed: {ex}");
        }
    }

    private SimCardInfo? PreferredSim()
    {
        var subId = _settings?.GetPreferredSubId(_thread.ThreadId) ?? 0;
        return subId > 0 ? _sims.FirstOrDefault(s => s.SubId == subId) : null;
    }

    // The SIM the conversation uses: the one chosen for it, else the one its messages came through.
    private SimCardInfo? EffectiveSim() =>
        PreferredSim() ?? _sims.FirstOrDefault(s => s.SubId == _thread.SubId);

    private void UpdateSim()
    {
        var loc = LocalizationManager.Instance;
        var preferred = PreferredSim();
        var effective = EffectiveSim();

        SimRow.IsVisible = _sims.Count > 1;
        PreferredSimValue.Text = preferred is null ? loc["Profile_SimPickerAutomatic"] : $"SIM {preferred.SlotIndex}";
        PreferredSimHint.Text = preferred is null
            ? loc["Profile_SimAutomatic"]
            : string.Format(loc["Profile_SimAlways"], SimLabel(preferred));

        var show = effective is not null && _sims.Count > 1;
        SimPill.IsVisible = show;
        if (effective is not null)
            SimPillLabel.Text = SimLabel(effective);
    }

    private static string SimLabel(SimCardInfo sim) =>
        string.IsNullOrWhiteSpace(sim.DisplayName) ? $"SIM {sim.SlotIndex}" : $"SIM {sim.SlotIndex} · {sim.DisplayName}";

    private async void OnPreferredSimTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (_settings is null || _sims.Count < 2)
                return;

            var loc = LocalizationManager.Instance;
            var automatic = loc["Profile_SimPickerAutomatic"];
            var options = new List<string> { automatic };
            options.AddRange(_sims.Select(SimLabel));

            var choice = await Dialogs.ActionSheetAsync(
                loc["Profile_SimPickerTitle"], loc["Chat_Cancel"], null, [.. options]);
            if (string.IsNullOrWhiteSpace(choice) || choice == loc["Chat_Cancel"])
                return;

            var index = options.IndexOf(choice);
            if (index < 0)
                return;

            _settings.SetPreferredSubId(_thread.ThreadId, index == 0 ? 0 : _sims[index - 1].SubId);
            UpdateSim();

            // An open chat keeps its cached state; the next open picks the new SIM up from the settings.
            ChatViewModel.EvictCache(_thread.ThreadId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Choosing the preferred SIM failed: {ex}");
        }
    }

    // ---- Links ---------------------------------------------------------------------------------

    private IReadOnlyList<LinkResultItem> _links = [];

    // Reads every message of the conversation, so it waits for the slide: the allocations it makes
    // would otherwise pause the slide for garbage collections.
    private async Task LoadLinksAsync(int version)
    {
        if (_sms is not { } sms)
            return;

        try
        {
            var threadId = _thread.ThreadId;
            var items = await Task.Run(async () =>
            {
                var rows = await sms.GetThreadTextRowsAsync(threadId);
                return BuildLinkItems(ThreadLinks.Find(rows), threadId);
            });
            if (version != _bindVersion)
                return;

            _links = items;
            var loc = LocalizationManager.Instance;
            LinksHint.Text = items.Count == 0
                ? loc["Profile_LinksNone"]
                : string.Format(loc["Profile_LinksCount"], items.Count);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Reading links for the profile failed: {ex}");
            if (version == _bindVersion)
                LinksHint.Text = LocalizationManager.Instance["Profile_LinksNone"];
        }
    }

    private List<LinkResultItem> BuildLinkItems(IReadOnlyList<ThreadLink> links, long threadId)
    {
        var now = DateTime.Now;
        var culture = CultureInfo.CurrentUICulture;
        var items = new List<LinkResultItem>(links.Count);
        foreach (var link in links)
        {
            var time = DateTimeOffset.FromUnixTimeMilliseconds(link.DateMs).LocalDateTime;
            items.Add(new LinkResultItem
            {
                ThreadId = threadId,
                MessageId = link.MessageId,
                Title = link.Item.Title,
                HostText = link.Item.Host,
                OpenUrl = link.Item.OpenUrl,
                // The page is already about one conversation, so the line under a link names its site, not the chat.
                ChatName = link.Item.Host,
                Time = _dates.FormatThreadTime(time, now, culture)
            });
        }

        return items;
    }

    private void OnLinksTapped(object? sender, TappedEventArgs e)
    {
        var loc = LocalizationManager.Instance;
        LinksList.ItemsSource = _links;
        LinksEmpty.Text = loc["Profile_LinksNone"];
        LinksEmpty.IsVisible = _links.Count == 0;
        LinksList.IsVisible = _links.Count > 0;
        LinksPage.IsVisible = true;
    }

    private void OnLinksBackTapped(object? sender, TappedEventArgs e) => LinksPage.IsVisible = false;

    // One handler for the whole list: a row's children inherit its data context.
    private async void OnLinkTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if ((e.Source as StyledElement)?.DataContext is LinkResultItem link && Uri.TryCreate(link.OpenUrl, UriKind.Absolute, out var uri))
                await Launcher.Default.OpenAsync(uri);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Opening a link failed: {ex}");
        }
    }

    // ---- Spam ----------------------------------------------------------------------------------

    // The profile shows a contact by name; a number or sender id is shown as itself.
    private bool IsContact =>
        _thread != ThreadItem.Empty && !string.Equals(_thread.Name, _thread.Phone, StringComparison.Ordinal);

    private void UpdateSpam()
    {
        if (_spam is null || _thread == ThreadItem.Empty)
            return;

        var loc = LocalizationManager.Instance;
        var isContact = IsContact;
        SetSwitch(SpamImmuneSwitch, SpamImmuneThumb, isContact || _spam.IsTrusted(_sendAddress), enabled: !isContact);
        SpamImmuneHint.Text = isContact ? loc["Profile_SpamImmuneContact"] : loc["Profile_SpamImmuneHint"];

        _spamItems = _spam.ForSender(_sendAddress);
        SpamMessagesRow.IsVisible = _spamItems.Count > 0;
        SpamMessagesHint.Text = string.Format(loc["Profile_SpamMessagesHint"], _spamItems.Count);

        if (SpamListPage.IsVisible)
        {
            SpamList.ItemsSource = _spamItems;
            SpamListPage.IsVisible = _spamItems.Count > 0;
        }
    }

    private void OnSpamImmuneTapped(object? sender, TappedEventArgs e)
    {
        if (_spam is null || IsContact)
            return;

        _spam.SetTrusted(_sendAddress, !_spam.IsTrusted(_sendAddress));
        UpdateSpam();
    }

    private void OnSpamMessagesTapped(object? sender, TappedEventArgs e)
    {
        SpamList.ItemsSource = _spamItems;
        SpamListPage.IsVisible = true;
    }

    private void OnSpamListBackTapped(object? sender, TappedEventArgs e) => SpamListPage.IsVisible = false;

    private void OnSpamListTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Control { DataContext: SpamItem item } source && source.Classes.Contains("more"))
            SpamMenuRequested?.Invoke(this, item);
    }

    // ---- Notifications and snooze -------------------------------------------------------------

    private void UpdateNotificationState()
    {
        var address = _sendAddress;
        _ = Task.Run(() =>
        {
            var custom = ProfileLauncher.HasCustomNotifications(address);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (address == _sendAddress)
                    NotificationsCustom.IsVisible = custom;
            });
        });
    }

    private void OnNotificationsTapped(object? sender, TappedEventArgs e) =>
        ProfileLauncher.OpenNotificationSettings(_sendAddress, _thread.Name);

    private long SnoozedUntil()
    {
        var until = _settings?.GetSnoozedUntil(_thread.ThreadId) ?? 0;
        return SnoozeSchedule.IsActive(until, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) ? until : 0;
    }

    private void UpdateSnooze()
    {
        var loc = LocalizationManager.Instance;
        var until = SnoozedUntil();
        SetSwitch(SnoozeSwitch, SnoozeThumb, until > 0);
        SnoozeHint.Text = until switch
        {
            0 => loc["Profile_SnoozeOff"],
            SnoozeSchedule.Indefinite => loc["Profile_SnoozeForever"],
            _ => string.Format(loc["Profile_SnoozeUntil"], FormatSnoozeEnd(until))
        };
    }

    private string FormatSnoozeEnd(long untilMillis)
    {
        var end = DateTimeOffset.FromUnixTimeMilliseconds(untilMillis).LocalDateTime;
        var now = DateTime.Now;
        var culture = CultureInfo.CurrentUICulture;
        var time = _dates.FormatMessageTime(end, culture);
        return end.Date == now.Date ? time : $"{_dates.FormatDateSeparator(end, now, culture)} {time}";
    }

    private async void OnSnoozeTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (_settings is null)
                return;

            // Turning it off needs no question.
            if (SnoozedUntil() > 0)
            {
                _settings.SetSnoozedUntil(_thread.ThreadId, 0);
                UpdateSnooze();
                return;
            }

            var loc = LocalizationManager.Instance;
            var presets = SnoozeSchedule.Presets;
            var labels = presets.Select(p => loc[SnoozeLabelKey(p)]).ToArray();

            var choice = await Dialogs.ActionSheetAsync(
                loc["Profile_SnoozeTitle"], loc["Chat_Cancel"], null, labels);
            var index = Array.IndexOf(labels, choice);
            if (index < 0)
                return;

            _settings.SetSnoozedUntil(_thread.ThreadId, SnoozeSchedule.UntilMillis(presets[index], DateTimeOffset.Now));
            UpdateSnooze();

            // What is already in the shade is quiet from now on too.
            var threadId = _thread.ThreadId;
            _ = Task.Run(() => MessageNotifier.Cancel(Platform.AppContext, threadId));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Snoozing the conversation failed: {ex}");
        }
    }

    private static string SnoozeLabelKey(SnoozePreset preset) => preset switch
    {
        SnoozePreset.OneHour => "Snooze_OneHour",
        SnoozePreset.EightHours => "Snooze_EightHours",
        SnoozePreset.TomorrowMorning => "Snooze_TomorrowMorning",
        SnoozePreset.OneWeek => "Snooze_OneWeek",
        _ => "Snooze_Forever"
    };

    // ---- Archive -------------------------------------------------------------------------------

    private void BindArchive()
    {
        var loc = LocalizationManager.Instance;
        ArchiveIcon.Data = IconCatalog.Find(_isArchived ? "unarchive.png" : "archive.png");
        ArchiveTitle.Text = loc[_isArchived ? "Profile_Unarchive" : "Profile_Archive"];
        ArchiveHint.Text = loc[_isArchived ? "Profile_UnarchiveHint" : "Profile_ArchiveHint"];
    }

    private void OnArchiveTapped(object? sender, TappedEventArgs e) => ArchiveRequested?.Invoke(this, !_isArchived);

    // ---- Block ---------------------------------------------------------------------------------

    private void BindBlocked()
    {
        BlockRow.IsVisible = _blocked is not null && !string.IsNullOrWhiteSpace(_sendAddress);
        if (!BlockRow.IsVisible)
            return;

        var blocked = _blocked!.IsBlocked(_sendAddress);
        var loc = LocalizationManager.Instance;
        BlockTitle.Text = loc[blocked ? "Profile_Unblock" : "Profile_Block"];
        BlockHint.Text = loc[blocked ? "Profile_UnblockHint" : "Profile_BlockHint"];
        BlockTitle.Foreground = blocked ? RowTitleBrush : BlockTitleBrush;
    }

    private async void OnBlockTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (_blocked is null || string.IsNullOrWhiteSpace(_sendAddress))
                return;

            var loc = LocalizationManager.Instance;
            var address = _sendAddress;
            if (_blocked.IsBlocked(address))
            {
                _blocked.SetBlocked(address, false);
                Toast.Show(loc["Profile_Unblocked"]);
            }
            else
            {
                var title = string.Format(loc["Profile_BlockConfirmTitle"], _thread.Name);
                if (!await Dialogs.AlertAsync(title, loc["Profile_BlockConfirmMessage"], loc["Profile_BlockConfirm"], loc["Chat_Cancel"]))
                    return;

                _blocked.SetBlocked(address, true);
                Toast.Show(loc["Profile_Blocked"]);
            }

            if (address == _sendAddress)
                BindBlocked();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Blocking the sender failed: {ex}");
        }
    }

    // ---- Quick actions -------------------------------------------------------------------------

    private void OnBackTapped(object? sender, TappedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    private void OnCallTapped(object? sender, TappedEventArgs e) => ProfileLauncher.Dial(_sendAddress);

    private void OnTextTapped(object? sender, TappedEventArgs e) => TextRequested?.Invoke(this, EventArgs.Empty);

    private void OnDetailsTapped(object? sender, TappedEventArgs e) => ProfileLauncher.ShowContact(_sendAddress);

    private void OnSearchTapped(object? sender, TappedEventArgs e) => SearchRequested?.Invoke(this, EventArgs.Empty);
}
