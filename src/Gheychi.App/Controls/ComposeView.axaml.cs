using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Gheychi.App.Localization;
using Gheychi.App.Platforms.Android;
using Gheychi.App.Ui;
using Gheychi.App.ViewModels;
using Gheychi.Core.Models;
using Gheychi.Core.Services;

namespace Gheychi.App.Controls;

public partial class ComposeView : UserControl
{
    private const long ContactsMaxAgeMs = 60_000;

    // Past this many insert/remove/replace steps one reset is cheaper than replaying them.
    private const int MaxIncrementalChanges = 24;

    // Everything about the address book that does not change between keystrokes, built off the UI thread.
    private sealed record BuiltContacts(ContactIndex Index, ComposeRow[] Rows, Dictionary<string, ComposeRow> Headers);

    private readonly FastObservableCollection<ComposeRow> _rows = [];
    private BuiltContacts? _contacts;
    private IReadOnlyList<ComposeRow> _recent = [];
    private string _recentKey = string.Empty;
    private IReadOnlyList<SimCardInfo> _sims = [];
    private int _simIndex;
    private bool _simPicked;
    private bool _hasContactsPermission = true;
    private bool _rebuildAfterOpen;
    private long _contactsLoadedTick;
    private long _lastChosenTick;
    private Task? _contactsTask;
    private ScrollViewer? _scroller;

    public event EventHandler? BackRequested;
    public event EventHandler<ComposeRecipient>? RecipientChosen;

    public ComposeView()
    {
        InitializeComponent();
        ContactList.ItemsSource = _rows;
        ContactList.TemplateApplied += (_, e) => _scroller = e.NameScope.Find<ScrollViewer>("PART_Scroller");
        UpdateStates(filtering: false, rowCount: 0);
    }

    /// <summary>The SIM picked in the To field; null on a single-SIM phone (the chat then uses its default).</summary>
    public SimCardInfo? SelectedSim => _sims.Count > 1 && _simIndex < _sims.Count ? _sims[_simIndex] : null;

    /// <summary>Reads the address book ahead of the first open so the list is there when the screen slides in.</summary>
    public Task PreloadContactsAsync() => EnsureContactsAsync(force: false);

    /// <summary>Sets the suggestions while the screen is parked, so the first open has nothing to rebuild.</summary>
    public void SetRecent(IReadOnlyList<ComposeRow> recent)
    {
        if (TakeRecent(recent))
            Rebuild();
    }

    /// <summary>Cheap work before the slide-in: start offscreen. The list is already at the top (see ResetState).</summary>
    public void PrepareForOpen(IReadOnlyList<ComposeRow> recent, double distanceDp)
    {
        SetTranslationY(distanceDp);

        // Changing the list is the expensive part of opening; with rows already there it waits for
        // the slide and the screen arrives with the previous suggestions for a moment.
        if (TakeRecent(recent))
        {
            if (_rows.Count == 0)
                Rebuild();
            else
                _rebuildAfterOpen = true;
        }

        _ = LoadSimsAsync();
    }

    /// <summary>After the slide-in: a contact refresh must not compete with the animation. The keyboard stays closed until the field is tapped.</summary>
    public void OnOpened()
    {
        if (_rebuildAfterOpen)
        {
            _rebuildAfterOpen = false;
            Rebuild();
        }

        _ = EnsureContactsAsync(force: false);
    }

    // True when the suggestions differ from the ones the list was built with.
    private bool TakeRecent(IReadOnlyList<ComposeRow> recent)
    {
        var key = string.Join('|', recent.Select(r => r.Address + r.Name));
        if (key == _recentKey)
            return false;

        _recentKey = key;
        _recent = recent;
        return true;
    }

    public void PrepareForClose() => Unfocus();

    /// <summary>Moves the finished screen out of the way and forgets what the user did in it.</summary>
    public void Park()
    {
        ResetState();

        // Collapsed while parked so page layout skips it; MessagesPage shows it again before the slide.
        IsVisible = false;
    }

    public void ResetState()
    {
        Unfocus();
        if (!string.IsNullOrEmpty(RecipientEntry.Text))
            RecipientEntry.Text = string.Empty;
        _simPicked = false;
        _simIndex = DefaultSimIndex();
        UpdateSimChip();
        UpdateCardFocus(false);
        ScrollToTop();
    }

    // Returns true when it consumed the back press.
    public bool HandleBack()
    {
        if (!RecipientEntry.IsFocused)
            return false;

        Unfocus();
        return true;
    }

    public Task SlideAsync(bool open, double distanceDp) =>
        OverlayAnimator.SlideYAsync(this, open ? distanceDp : 0, open ? 0 : distanceDp, open ? OverlayAnimator.OpenDuration : OverlayAnimator.CloseDuration, open);

    public void SetTranslationY(double dp) => OverlayAnimator.SetTranslation(this, 0, dp);

    public void ScrollToTop() => _scroller?.ScrollToHome();

    // Avalonia has no "clear focus": focus goes to the page itself, which closes the keyboard.
    private void Unfocus()
    {
        if (RecipientEntry.IsFocused)
            Root.Focus();
    }

    // ---- Data -----------------------------------------------------------------------------------

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        MainActivity.Resumed -= OnAppResumed;
        MainActivity.Resumed += OnAppResumed;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        MainActivity.Resumed -= OnAppResumed;
    }

    // Back from the system settings after allowing contacts access.
    private void OnAppResumed()
    {
        if (!_hasContactsPermission)
            _ = EnsureContactsAsync(force: true);
    }

    private async Task LoadSimsAsync()
    {
        try
        {
            var smsService = IPlatformApplication.Current?.Services.GetService<ISmsService>();
            if (smsService is null)
                return;

            var sims = await smsService.GetActiveSimsAsync();
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _sims = sims;
                if (!_simPicked || _simIndex >= sims.Count)
                    _simIndex = DefaultSimIndex();
                UpdateSimChip();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Compose SIM load failed: {ex}");
        }
    }

    private Task EnsureContactsAsync(bool force)
    {
        if (_contactsTask is { IsCompleted: false })
            return _contactsTask;

        var fresh = _contacts is not null && _hasContactsPermission &&
                    Environment.TickCount64 - _contactsLoadedTick < ContactsMaxAgeMs;
        if (!force && fresh)
            return Task.CompletedTask;

        return _contactsTask = LoadContactsAsync();
    }

    private async Task LoadContactsAsync()
    {
        try
        {
            var smsService = IPlatformApplication.Current?.Services.GetService<ISmsService>();
            if (smsService is null)
                return;

            var granted = await Permissions.CheckStatusAsync<ContactsPermission>() == PermissionStatus.Granted;
            IReadOnlyList<ContactEntry> entries = granted ? await smsService.GetContactsAsync() : Array.Empty<ContactEntry>();
            var built = await Task.Run(() => BuildContacts(entries));
            _contactsLoadedTick = Environment.TickCount64;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    ApplyContacts(built, granted);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Compose contacts apply failed: {ex}");
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Compose contacts load failed: {ex}");
        }
    }

    private static BuiltContacts BuildContacts(IReadOnlyList<ContactEntry> entries)
    {
        var index = new ContactIndex(entries);
        var rows = new ComposeRow[index.Count];
        var headers = new Dictionary<string, ComposeRow>();

        for (var i = 0; i < rows.Length; i++)
        {
            var contact = index[i];
            var letter = index.LetterAt(i);
            if (!headers.ContainsKey(letter))
                headers[letter] = new ComposeRow { Kind = ComposeRowKind.Header, Letter = letter };

            rows[i] = new ComposeRow
            {
                Kind = ComposeRowKind.Contact,
                Name = contact.Name,
                ContactName = contact.Name,
                Address = contact.Number,
                Initials = letter == ContactListBuilder.OtherLetter ? string.Empty : ThreadItem.GenerateInitials(contact.Name),
                Subtitle = $"{contact.Label} · {LtrNumbers.Wrap(PhoneNumberNormalizer.FormatDisplay(contact.Number))}"
            };
        }

        return new BuiltContacts(index, rows, headers);
    }

    private void ApplyContacts(BuiltContacts built, bool granted)
    {
        // An unchanged address book must not touch the list: that would reset what the user scrolled to.
        var changed = _contacts is null ||
                      _contacts.Index.Signature != built.Index.Signature ||
                      granted != _hasContactsPermission;
        _hasContactsPermission = granted;
        if (!changed)
            return;

        _contacts = built;
        Rebuild();
    }

    private void Rebuild()
    {
        var query = RecipientEntry.Text ?? string.Empty;
        var filtering = !string.IsNullOrWhiteSpace(query);
        var loc = LocalizationManager.Instance;
        var contacts = _contacts;

        var rows = new List<ComposeRow>((contacts?.Rows.Length ?? 0) + _recent.Count + 32);

        var typed = ContactListBuilder.TypedAddress(query);
        if (typed.Length > 0)
        {
            rows.Add(new ComposeRow
            {
                Kind = ComposeRowKind.Typed,
                Address = typed,
                Name = string.Format(loc["Compose_SendTo"], LtrNumbers.Wrap(PhoneNumberNormalizer.FormatDisplay(typed))),
                Subtitle = loc["Compose_NewNumber"]
            });
        }

        if (!filtering && _recent.Count > 0)
        {
            rows.Add(new ComposeRow { Kind = ComposeRowKind.Header, Letter = loc["Compose_Recent"] });
            rows.AddRange(_recent);
        }

        if (contacts is not null && contacts.Rows.Length > 0)
        {
            // Filtered results are ranked by relevance, so section titles only make sense unfiltered.
            string? section = null;
            foreach (var i in contacts.Index.Match(query))
            {
                if (!filtering)
                {
                    var letter = contacts.Index.LetterAt(i);
                    if (letter != section)
                    {
                        section = letter;
                        rows.Add(contacts.Headers[letter]);
                    }
                }

                rows.Add(contacts.Rows[i]);
            }
        }

        var rowCount = rows.Count;
        if (rowCount > 0)
            rows.Add(ComposeRowTemplateSelector.Footer);

        ApplyRows(rows);
        UpdateStates(filtering, rowCount);
    }

    // The contact and section rows are built once per address book load, so most of a new list is the old one:
    // only the part between the shared start and end is touched. Typing narrows the list without rebinding the rows that stay.
    private void ApplyRows(List<ComposeRow> rows)
    {
        var current = _rows;
        var shared = Math.Min(current.Count, rows.Count);

        var prefix = 0;
        while (prefix < shared && ReferenceEquals(current[prefix], rows[prefix]))
            prefix++;

        var suffix = 0;
        while (suffix < shared - prefix && ReferenceEquals(current[current.Count - 1 - suffix], rows[rows.Count - 1 - suffix]))
            suffix++;

        var removing = current.Count - prefix - suffix;
        var adding = rows.Count - prefix - suffix;
        if (removing == 0 && adding == 0)
            return;

        if (removing + adding > MaxIncrementalChanges)
        {
            current.Reset(rows);
            return;
        }

        var replaced = Math.Min(removing, adding);
        for (var i = 0; i < replaced; i++)
            current[prefix + i] = rows[prefix + i];
        for (var i = replaced; i < removing; i++)
            current.RemoveAt(prefix + replaced);
        for (var i = replaced; i < adding; i++)
            current.Insert(prefix + i, rows[prefix + i]);
    }

    private void UpdateStates(bool filtering, int rowCount)
    {
        var empty = rowCount == 0;
        var loading = empty && _contacts is null;
        var showEmpty = empty && !loading;

        LoadingIndicator.IsVisible = loading;
        LoadingIndicator.IsIndeterminate = loading;
        ContactList.IsVisible = !empty;
        EmptyState.IsVisible = showEmpty;
        if (!showEmpty)
            return;

        var loc = LocalizationManager.Instance;
        string prefix;
        if (filtering)
            prefix = "Compose_NoMatches";
        else if (!_hasContactsPermission)
            prefix = "Compose_NoPermission";
        else
            prefix = "Compose_NoContacts";

        EmptyTitle.Text = loc[prefix];
        EmptyHint.Text = loc[prefix + "Hint"];
        EmptyAction.IsVisible = !filtering && !_hasContactsPermission;
    }

    // ---- Recipient field --------------------------------------------------------------------------

    private void UpdateSimChip()
    {
        var sim = SelectedSim;
        SimChip.IsVisible = sim is not null;
        if (sim is not null)
            SimLabel.Text = $"SIM {sim.SlotIndex}";
    }

    private void UpdateCardFocus(bool focused) =>
        RecipientCard.BorderBrush = focused ? Palette.Pick("#2E7D5B", "#6FD3A8") : Palette.Transparent;

    private void Choose(ComposeRow row)
    {
        if (string.IsNullOrWhiteSpace(row.Address))
            return;

        // A tap can be reported twice on some devices; one conversation is enough.
        var now = Environment.TickCount64;
        if (now - _lastChosenTick < 500)
            return;
        _lastChosenTick = now;

        RecipientChosen?.Invoke(this, new ComposeRecipient(row.Address, row.ContactName, SelectedSim));
    }

    // One handler for the whole list: a row's children inherit its data context.
    private void OnRowTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as StyledElement)?.DataContext is ComposeRow { Kind: not ComposeRowKind.Header } row)
            Choose(row);
    }

    private void OnRecipientTextChanged(object? sender, TextChangedEventArgs e)
    {
        ClearButton.IsVisible = !string.IsNullOrEmpty(RecipientEntry.Text);
        Rebuild();
    }

    private void OnRecipientFocusChanged(object? sender, RoutedEventArgs e) => UpdateCardFocus(RecipientEntry.IsFocused);

    private void OnRecipientKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        CompleteRecipient();
    }

    // Go sends to the typed number, or to the only contact still matching the text.
    private void CompleteRecipient()
    {
        var typed = _rows.FirstOrDefault(r => r.Kind == ComposeRowKind.Typed);
        if (typed is not null)
        {
            Choose(typed);
            return;
        }

        var contacts = _rows.Where(r => r.Kind == ComposeRowKind.Contact).Take(2).ToList();
        if (contacts.Count == 1)
            Choose(contacts[0]);
    }

    private void OnClearTapped(object? sender, TappedEventArgs e)
    {
        RecipientEntry.Text = string.Empty;
        RecipientEntry.Focus();
    }

    private int DefaultSimIndex()
    {
        var subId = Gheychi.App.Services.AppPreferences.DefaultSubId;
        for (var i = 0; i < _sims.Count; i++)
        {
            if (_sims[i].SubId == subId)
                return i;
        }

        return 0;
    }

    private void OnSimChipTapped(object? sender, TappedEventArgs e)
    {
        if (_sims.Count <= 1)
            return;

        _simIndex = (_simIndex + 1) % _sims.Count;
        _simPicked = true;
        UpdateSimChip();
    }

    private void OnBackTapped(object? sender, TappedEventArgs e)
    {
        Unfocus();
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnEmptyActionTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            AppInfo.Current.ShowSettingsUI();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Open settings failed: {ex.Message}");
        }
    }
}
