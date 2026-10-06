using Gheychi.App.Gestures;
using Gheychi.App.Localization;
using Gheychi.App.ViewModels;
using Gheychi.Core.Models;
using Gheychi.Core.Services;

namespace Gheychi.App.Controls;

public partial class ComposeView : ContentView
{
    private const long ContactsMaxAgeMs = 60_000;

    // Everything about the address book that does not change between keystrokes, built off the UI thread.
    private sealed record BuiltContacts(ContactIndex Index, ComposeRow[] Rows, Dictionary<string, ComposeRow> Headers);

    private readonly FastObservableCollection<ComposeRow> _rows = [];
    private BuiltContacts? _contacts;
    private IReadOnlyList<ComposeRow> _recent = [];
    private string _recentKey = string.Empty;
    private IReadOnlyList<SimCardInfo> _sims = [];
    private int _simIndex;
    private bool _hasContactsPermission = true;
    private long _contactsLoadedTick;
    private long _lastChosenTick;
    private Task? _contactsTask;

    public event EventHandler? BackRequested;
    public event EventHandler<ComposeRecipient>? RecipientChosen;

    public ComposeView()
    {
        InitializeComponent();
        ContactList.ItemsSource = _rows;
        ListTuning.UseFixedSize(ContactList);
        UpdateStates(filtering: false, rowCount: 0);
        Loaded += OnViewLoaded;
    }

    /// <summary>The SIM picked in the To field; null on a single-SIM phone (the chat then uses its default).</summary>
    public SimCardInfo? SelectedSim => _sims.Count > 1 && _simIndex < _sims.Count ? _sims[_simIndex] : null;

    /// <summary>Reads the address book ahead of the first open so the list is there when the screen slides in.</summary>
    public Task PreloadContactsAsync() => EnsureContactsAsync(force: false);

    /// <summary>Sets the suggestions while the screen is parked (warm-up), so the first open has nothing to rebuild.</summary>
    public void SetRecent(IReadOnlyList<ComposeRow> recent)
    {
        if (TakeRecent(recent))
            Rebuild();
    }

    /// <summary>Cheap work before the slide-in: start offscreen. The list is already at the top (see ResetState).</summary>
    public void PrepareForOpen(IReadOnlyList<ComposeRow> recent, double distanceDp)
    {
        SetTranslationY(distanceDp);

        // Re-binding the list is the expensive part of opening; with rows already there it waits for
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

    private bool _rebuildAfterOpen;

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

    public void PrepareForClose() => RecipientEntry.Unfocus();

    /// <summary>Moves the finished screen out of the way and forgets what the user did in it.</summary>
    public void Park()
    {
        InputTransparent = true;
        SetTranslationY(OverlayAnimator.ParkedDistance);
        ResetState();

        // Collapsed while parked so page resizes skip it; MessagesPage shows it again before the slide.
        IsVisible = false;
    }

    public void ResetState()
    {
        RecipientEntry.Unfocus();
        if (!string.IsNullOrEmpty(RecipientEntry.Text))
            RecipientEntry.Text = string.Empty;
        _simIndex = 0;
        UpdateSimChip();
        UpdateCardFocus(false);
        ScrollToTop();
    }

    // Returns true when it consumed the back press.
    public bool HandleBack()
    {
        if (!RecipientEntry.IsFocused)
            return false;

        RecipientEntry.Unfocus();
        return true;
    }

    public Task SlideAsync(bool open, double distanceDp) =>
        OverlayAnimator.SlideYAsync(this, open ? distanceDp : 0, open ? 0 : distanceDp, open ? OverlayAnimator.OpenDuration : OverlayAnimator.CloseDuration, open);

    public void SetTranslationY(double dp) => OverlayAnimator.SetTranslationY(this, dp);

    public void ScrollToTop()
    {
        try
        {
#if ANDROID
            if (ContactList.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView recycler)
            {
                recycler.StopScroll();
                recycler.ScrollToPosition(0);
                return;
            }
#endif
            if (_rows.Count > 0)
                ContactList.ScrollTo(0, position: ScrollToPosition.Start, animate: false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Compose scroll failed: {ex.Message}");
        }
    }

    // ---- Data -----------------------------------------------------------------------------------

    private void OnViewLoaded(object? sender, EventArgs e)
    {
        if (Window is { } window)
        {
            window.Resumed -= OnWindowResumed;
            window.Resumed += OnWindowResumed;
        }
    }

    // Back from the system settings after allowing contacts access.
    private void OnWindowResumed(object? sender, EventArgs e)
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
                if (_simIndex >= sims.Count)
                    _simIndex = 0;
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

            var granted = true;
#if ANDROID
            granted = await Microsoft.Maui.ApplicationModel.Permissions.CheckStatusAsync<Gheychi.App.Platforms.Android.Permissions.ContactsPermission>()
                      == Microsoft.Maui.ApplicationModel.PermissionStatus.Granted;
#endif
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
                Subtitle = $"{contact.Label} · {PhoneNumberNormalizer.FormatDisplay(contact.Number)}"
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
                Name = string.Format(loc["Compose_SendTo"], PhoneNumberNormalizer.FormatDisplay(typed)),
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

        _rows.Reset(rows);
        UpdateStates(filtering, rows.Count);
    }

    private void UpdateStates(bool filtering, int rowCount)
    {
        var empty = rowCount == 0;
        var loading = empty && _contacts is null;
        var showEmpty = empty && !loading;

        LoadingIndicator.IsVisible = loading;
        LoadingIndicator.IsRunning = loading;
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

    private static bool IsDark => Application.Current?.RequestedTheme == AppTheme.Dark;

    private void UpdateCardFocus(bool focused) =>
        RecipientCard.Stroke = focused
            ? new SolidColorBrush(IsDark ? Color.FromArgb("#8FE0BE") : Color.FromArgb("#2E6B4C"))
            : Brush.Transparent;

    private void Choose(ComposeRow row)
    {
        if (string.IsNullOrWhiteSpace(row.Address))
            return;

        // The row reports taps through two paths on some devices; one conversation is enough.
        var now = Environment.TickCount64;
        if (now - _lastChosenTick < 500)
            return;
        _lastChosenTick = now;

        RecipientChosen?.Invoke(this, new ComposeRecipient(row.Address, row.ContactName, SelectedSim));
    }

    private void OnRowTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is ComposeRow { Kind: not ComposeRowKind.Header } row)
            Choose(row);
    }

    private void OnRecipientTextChanged(object? sender, TextChangedEventArgs e)
    {
        ClearButton.IsVisible = !string.IsNullOrEmpty(e.NewTextValue);
        Rebuild();
    }

    private void OnRecipientFocusChanged(object? sender, FocusEventArgs e) => UpdateCardFocus(e.IsFocused);

    // Go sends to the typed number, or to the only contact still matching the text.
    private void OnRecipientCompleted(object? sender, EventArgs e)
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

    private void OnSimChipTapped(object? sender, TappedEventArgs e)
    {
        if (_sims.Count <= 1)
            return;

        _simIndex = (_simIndex + 1) % _sims.Count;
        UpdateSimChip();
    }

    private void OnBackTapped(object? sender, TappedEventArgs e)
    {
        RecipientEntry.Unfocus();
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
