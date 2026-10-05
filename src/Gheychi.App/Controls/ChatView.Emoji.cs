using Gheychi.App.Behaviors;
using Gheychi.App.ViewModels;
using Gheychi.Core.Services;

namespace Gheychi.App.Controls;

// The emoji picker under the message box: it takes the keyboard's place, and an emoji tapped in it goes into the draft.
public partial class ChatView
{
    private const double DefaultPanelHeight = 290;
    private const double MinKeyboardHeight = 150;
    private const string RecentKey = "recent_emojis_v1";
    private const int RecentTab = 0;

    private static readonly RecentEmojis Recent = LoadRecent();

    // Shared by every chat, so the picker opens at the height the keyboard last had.
    private static double _panelHeight = DefaultPanelHeight;

    private readonly List<Label> _emojiTabIcons = [];
    private bool _emojiTabsBuilt;
    private int _emojiTab = -1;

    // Where the next emoji goes. The message box is not focused while the picker is up, so the cursor is tracked here.
    private int _emojiCursor;

    public bool IsEmojiPanelOpen => EmojiPanel.IsVisible;

    private void InitializeEmoji()
    {
        // Tapping into the message box brings the keyboard back, so the picker has to make room for it.
        MessageEntry.Focused += (_, _) => HideEmojiPanel(focusMessageBox: false);
    }

    private void OnEmojiButtonTapped(object? sender, TappedEventArgs e)
    {
        if (EmojiPanel.IsVisible)
            HideEmojiPanel(focusMessageBox: true);
        else
            ShowEmojiPanel();
    }

    private void ShowEmojiPanel()
    {
        if (Vm is not { } vm)
            return;

        EnsureEmojiTabs();

        var draft = vm.Draft ?? string.Empty;
        _emojiCursor = MessageEntry.IsFocused ? Math.Clamp(MessageEntry.CursorPosition, 0, draft.Length) : draft.Length;

        if (_keyboardOffset >= MinKeyboardHeight)
            _panelHeight = _keyboardOffset;

        EmojiPanel.HeightRequest = _panelHeight;
        EmojiPanel.IsVisible = true;
        EmojiToggleIcon.Source = "keyboard.png";
        SelectEmojiTab(_emojiTab >= 0 ? _emojiTab : (Recent.Items.Count > 0 ? RecentTab : 1));

        // The picker replaces the keyboard; the keyboard goes away as the picker comes up.
        MessageEntry.Unfocus();

        if (_followTail)
        {
            _scrolledToEnd = false;
            ScrollToEnd(false);
        }
    }

    private void HideEmojiPanel(bool focusMessageBox)
    {
        if (EmojiPanel.IsVisible)
        {
            EmojiPanel.IsVisible = false;
            EmojiToggleIcon.Source = "emoji.png";
        }

        if (focusMessageBox)
            MessageEntry.Focus();
    }

    // ---- Tabs ----------------------------------------------------------------------------------

    // Built the first time the picker opens: most chats never use it.
    private void EnsureEmojiTabs()
    {
        if (_emojiTabsBuilt)
            return;
        _emojiTabsBuilt = true;

        var categories = EmojiCatalog.Categories;
        var tabCount = categories.Count + 1;
        for (var i = 0; i < tabCount; i++)
            EmojiTabs.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        EmojiTabs.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        for (var i = 0; i < tabCount; i++)
        {
            var index = i;
            var icon = new Label
            {
                Text = i == RecentTab ? "🕘" : categories[i - 1].Icon,
                FontSize = 20,
                Opacity = 0.4,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                BackgroundColor = Colors.Transparent
            };
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => SelectEmojiTab(index);
            icon.GestureRecognizers.Add(tap);

            EmojiTabs.Add(icon, i, 0);
            _emojiTabIcons.Add(icon);
        }

        var backspace = new Image
        {
            Source = "backspace.png",
            WidthRequest = 22,
            HeightRequest = 22,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            BackgroundColor = Colors.Transparent
        };
        var tint = new IconTintBehavior();
        tint.SetAppThemeColor(IconTintBehavior.TintColorProperty, Color.FromArgb("#5C6370"), Color.FromArgb("#9AA0AB"));
        backspace.Behaviors.Add(tint);
        var delete = new TapGestureRecognizer();
        delete.Tapped += OnEmojiBackspaceTapped;
        backspace.GestureRecognizers.Add(delete);
        EmojiTabs.Add(backspace, tabCount, 0);
    }

    private void SelectEmojiTab(int index)
    {
        _emojiTab = index;
        for (var i = 0; i < _emojiTabIcons.Count; i++)
            _emojiTabIcons[i].Opacity = i == index ? 1 : 0.4;

        // A snapshot: the recent list moves as emoji are picked, and the grid must not shuffle under the finger.
        EmojiList.ItemsSource = index == RecentTab
            ? Recent.Items.ToList()
            : EmojiCatalog.Categories[index - 1].Emojis;
    }

    // ---- Editing the draft ---------------------------------------------------------------------

    private void OnEmojiTapped(object? sender, TappedEventArgs e)
    {
        if (Vm is not { } vm || sender is not Label { Text: { Length: > 0 } emoji })
            return;

        var (text, cursor) = EmojiText.Insert(vm.Draft, _emojiCursor, emoji);
        ApplyDraft(vm, text, cursor);

        Recent.Add(emoji);
        SaveRecent();
    }

    private void OnEmojiBackspaceTapped(object? sender, TappedEventArgs e)
    {
        if (Vm is not { } vm)
            return;

        var (text, cursor) = EmojiText.DeleteBefore(vm.Draft, _emojiCursor);
        ApplyDraft(vm, text, cursor);
    }

    private void ApplyDraft(ChatViewModel vm, string text, int cursor)
    {
        vm.Draft = text;
        _emojiCursor = cursor;
        MessageEntry.CursorPosition = cursor;
    }

    // ---- Recent emoji --------------------------------------------------------------------------

    private static RecentEmojis LoadRecent()
    {
        try
        {
            return RecentEmojis.Parse(Preferences.Default.Get(RecentKey, string.Empty));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Reading recent emoji failed: {ex}");
            return new RecentEmojis();
        }
    }

    private static void SaveRecent()
    {
        try
        {
            Preferences.Default.Set(RecentKey, Recent.Serialize());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Saving recent emoji failed: {ex}");
        }
    }
}
