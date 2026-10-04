using Gheychi.App.Localization;
using Gheychi.App.ViewModels;
using Gheychi.Core.Models;
using Gheychi.Core.Services;

namespace Gheychi.App.Controls;

public partial class ComposeView : ContentView
{
    private const long ContactsMaxAgeMs = 60_000;

    private readonly FastObservableCollection<ComposeRow> _rows = [];
    private readonly List<string> _letters = [];
    private readonly Dictionary<string, int> _letterRows = [];
    private List<ContactEntry> _contacts = [];
    private IReadOnlyList<SimCardInfo> _sims = [];
    private int _simIndex;
    private bool _numericKeyboard;
    private bool _hasContactsPermission = true;
    private long _contactsLoadedTick;
    private Task? _contactsTask;
    private string? _lastJumpLetter;

    public event EventHandler? BackRequested;
    public event EventHandler<ComposeRecipient>? RecipientChosen;

    public ComposeView()
    {
        InitializeComponent();
        ContactList.ItemsSource = _rows;
        LetterStrip.IsVisible = false;
#if ANDROID
        LetterStrip.HandlerChanged += (_, _) => AttachStripTouch();
#endif
    }

    public SimCardInfo? SelectedSim => _sims.Count > 1 && _simIndex < _sims.Count ? _sims[_simIndex] : null;

    /// <summary>Loads (or refreshes) the contacts and SIM list; call when the screen opens.</summary>
    public Task PrepareAsync()
    {
        _ = LoadSimsAsync();
        return EnsureContactsAsync();
    }

    public void FocusInput()
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                await Task.Delay(100);
                RecipientEntry.Focus();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Compose focus failed: {ex}");
            }
        });
    }

    public void ResetState()
    {
        RecipientEntry.Unfocus();
        RecipientEntry.Text = string.Empty;
        SetNumericKeyboard(false);
        _lastJumpLetter = null;
    }

    // Returns true when it consumed the back press.
    public bool HandleBack()
    {
        if (!RecipientEntry.IsFocused)
            return false;

        RecipientEntry.Unfocus();
        return true;
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
                var previous = SelectedSim?.SubId;
                _sims = sims;
                _simIndex = 0;
                if (previous is int subId)
                {
                    var index = sims.ToList().FindIndex(s => s.SubId == subId);
                    if (index >= 0)
                        _simIndex = index;
                }

                UpdateSimChip();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Compose SIM load failed: {ex}");
        }
    }

    private Task EnsureContactsAsync()
    {
        if (_contactsTask is { IsCompleted: false })
            return _contactsTask;

        if (_contactsLoadedTick != 0 && Environment.TickCount64 - _contactsLoadedTick < ContactsMaxAgeMs)
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
            granted = await Microsoft.Maui.ApplicationModel.Permissions.CheckStatusAsync<Gheychi.App.Platforms.Android.Permissions.ContactsPermission>() == Microsoft.Maui.ApplicationModel.PermissionStatus.Granted;
#endif
            IReadOnlyList<ContactEntry> contacts = granted ? await smsService.GetContactsAsync() : Array.Empty<ContactEntry>();
            _contactsLoadedTick = Environment.TickCount64;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                _hasContactsPermission = granted;
                _contacts = [.. contacts];
                Rebuild();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Compose contacts load failed: {ex}");
        }
    }

    private void Rebuild()
    {
        var query = RecipientEntry.Text ?? string.Empty;
        var filtering = !string.IsNullOrWhiteSpace(query);
        var loc = LocalizationManager.Instance;
        var sections = ContactListBuilder.Build(_contacts, query);

        var rows = new List<ComposeRow>(_contacts.Count + sections.Count + 1);
        var typed = ContactListBuilder.TypedAddress(query);
        if (typed.Length > 0)
        {
            rows.Add(new ComposeRow
            {
                Kind = ComposeRowKind.Typed,
                Address = typed,
                Name = string.Format(loc["Compose_SendTo"], PhoneNumberNormalizer.FormatDisplay(typed))
            });
        }

        _letters.Clear();
        _letterRows.Clear();
        foreach (var section in sections)
        {
            _letters.Add(section.Letter);
            _letterRows[section.Letter] = rows.Count;
            rows.Add(new ComposeRow { Kind = ComposeRowKind.Header, Letter = section.Letter });

            foreach (var contact in section.Contacts)
            {
                rows.Add(new ComposeRow
                {
                    Kind = ComposeRowKind.Contact,
                    Name = contact.Name,
                    ContactName = contact.Name,
                    Address = contact.Number,
                    Initials = ThreadItem.GenerateInitials(contact.Name),
                    Subtitle = $"{contact.Label} · {PhoneNumberNormalizer.FormatDisplay(contact.Number)}"
                });
            }
        }

        _rows.Reset(rows);

        RebuildLetterStrip(!filtering && _letters.Count > 1);

        var empty = rows.Count == 0;
        EmptyLabel.IsVisible = empty;
        ContactList.IsVisible = !empty;
        if (empty)
        {
            EmptyLabel.Text = filtering
                ? loc["Compose_NoMatches"]
                : loc[_hasContactsPermission ? "Compose_NoContacts" : "Compose_NoPermission"];
        }
    }

    private void RebuildLetterStrip(bool visible)
    {
        LetterStrip.Children.Clear();
        LetterStrip.IsVisible = visible;
        if (!visible)
            return;

        foreach (var letter in _letters)
        {
            LetterStrip.Children.Add(new Label
            {
                Text = letter,
                FontSize = 10,
                HorizontalTextAlignment = TextAlignment.Center,
                FontFamily = ThreadItem.FontFamilyBold,
                TextColor = Color.FromArgb("#2E6B4C"),
                InputTransparent = true
            });
        }
    }

    private void JumpToLetter(string letter)
    {
        if (letter == _lastJumpLetter || !_letterRows.TryGetValue(letter, out var index))
            return;

        _lastJumpLetter = letter;
        try
        {
            ContactList.ScrollTo(index, position: ScrollToPosition.Start, animate: false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Compose scroll failed: {ex.Message}");
        }
    }

#if ANDROID
    private void AttachStripTouch()
    {
        if (LetterStrip.Handler?.PlatformView is not Android.Views.View native)
            return;

        native.Clickable = true;
        native.Touch -= OnStripTouch;
        native.Touch += OnStripTouch;
    }

    private void OnStripTouch(object? sender, Android.Views.View.TouchEventArgs e)
    {
        e.Handled = false;
        if (sender is not Android.Views.View view || e.Event is not { } motion || _letters.Count == 0)
            return;

        switch (motion.ActionMasked)
        {
            case Android.Views.MotionEventActions.Down:
            case Android.Views.MotionEventActions.Move:
                view.Parent?.RequestDisallowInterceptTouchEvent(true);
                var fraction = Math.Clamp(motion.GetY() / Math.Max(1, view.Height), 0, 0.999);
                JumpToLetter(_letters[(int)(fraction * _letters.Count)]);
                e.Handled = true;
                break;

            case Android.Views.MotionEventActions.Up:
            case Android.Views.MotionEventActions.Cancel:
                _lastJumpLetter = null;
                e.Handled = true;
                break;
        }
    }
#endif

    private void UpdateSimChip()
    {
        var sim = SelectedSim;
        SimChip.IsVisible = sim is not null;
        if (sim is not null)
            SimLabel.Text = $"SIM {sim.SlotIndex}";
    }

    private void SetNumericKeyboard(bool numeric)
    {
        _numericKeyboard = numeric;
        RecipientEntry.Keyboard = numeric ? Keyboard.Telephone : Keyboard.Default;
        DialpadButton.BackgroundColor = numeric
            ? (Application.Current?.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#2A302C") : Color.FromArgb("#E3EAE2"))
            : Colors.Transparent;
    }

    private void Choose(ComposeRow row)
    {
        if (string.IsNullOrWhiteSpace(row.Address))
            return;

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
        _lastJumpLetter = null;
        Rebuild();
    }

    // Enter sends to the typed number, or to the only contact still matching the text.
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

    private void OnDialpadTapped(object? sender, TappedEventArgs e)
    {
        SetNumericKeyboard(!_numericKeyboard);

        // The keyboard type only applies to a freshly shown keyboard.
        RecipientEntry.Unfocus();
        FocusInput();
    }

    private void OnBackTapped(object? sender, TappedEventArgs e)
    {
        RecipientEntry.Unfocus();
        BackRequested?.Invoke(this, EventArgs.Empty);
    }
}
