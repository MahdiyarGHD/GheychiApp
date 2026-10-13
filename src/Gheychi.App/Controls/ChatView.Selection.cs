using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Gheychi.App.Theming;
using Gheychi.App.Ui;
using Gheychi.App.ViewModels;
using Gheychi.Core.Services;

namespace Gheychi.App.Controls;

// Selecting part of a message. A double tap on the text selects the word under the finger (a number or a link whole)
// in a read-only box laid over the bubble's text block: the box brings the platform's handles and the text bar, and
// draws nothing but the selection, so the bubble's own text, links and numbers stay what is seen. A selected number
// that is a contact's gets the contact under the bar, to open that conversation.
public partial class ChatView
{
    private const char ZeroWidthNonJoiner = (char)0x200C;

    private static readonly IBrush OutgoingSelection = Palette.Brush("#55FFFFFF");
    private static readonly IBrush IncomingSelection = Palette.Accent(AccentRole.Solid, 80);

    private const double TapSlop = 12;

    private PointerPressedEventArgs? _lastPress;
    private Point _lastPressPoint;
    private IPointer? _selectionPointer;
    private TextBlock? _selectedBlock;
    private ChatMessage? _selectedMessage;
    private ChatMessage? _simToggled;
    private long _simToggledTick;
    private int _contactVersion;
    private ContactChip? _selectionContact;
    private bool _openingSelection;

    /// <summary>The contact under a selected or tapped number was tapped: their conversation is to be opened.</summary>
    public event Action<string>? ConversationRequested;

    private bool IsTextSelected => _selectedBlock is not null;

    private void InitTextSelection()
    {
        MessagesList.AddHandler(DoubleTappedEvent, OnMessageDoubleTapped);
        // A finger let go on the list hands the focus to the page (that is how a tap on it drops the keyboard), which
        // ends the selection. Right for a tap beside it; not for the finger that double tapped to make it, nor for one
        // that only scrolled.
        MessagesList.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            var making = ReferenceEquals(e.Pointer, _selectionPointer);
            _selectionPointer = null;
            if (IsTextSelected && (making || Point.Distance(e.GetPosition(this), _lastPressPoint) > TapSlop))
                e.Handled = true;
        }, RoutingStrategies.Tunnel);
        MessagesList.LayoutUpdated += (_, _) => PlaceSelectionBox();
        SelectionBox.PropertyChanged += (_, e) =>
        {
            if ((e.Property != TextBox.SelectionStartProperty && e.Property != TextBox.SelectionEndProperty) ||
                !IsTextSelected || _openingSelection)
                return;

            // Copied (the bar clears the selection), or a tap on the text that only moved the caret.
            if (SelectionBox.SelectionStart == SelectionBox.SelectionEnd)
                CloseTextSelection();
            else
                _ = ShowSelectionContactAsync(SelectionBox.SelectedText ?? string.Empty, settle: true);
        };
        SelectionBox.LostFocus += (_, _) => CloseTextSelection();
        LinkPopup.ConversationRequested += number => ConversationRequested?.Invoke(number);
    }

    private void NotePress(PointerPressedEventArgs e)
    {
        _lastPress = e;
        _lastPressPoint = e.GetPosition(this);
    }

    /// <summary>Remembered for a double tap: a tap on an incoming bubble shows or hides its SIM, which the double tap undoes.</summary>
    private void NoteSimToggled(ChatMessage message)
    {
        _simToggled = message;
        _simToggledTick = Environment.TickCount64;
    }

    private void OnMessageDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_stoppedFling || Vm is not { IsSelectionMode: false } vm || PartOf(e.Source as Visual) != "bubble" ||
            (e.Source as StyledElement)?.DataContext is not ChatMessage message || BodyOf(e.Source as Visual) is not { } block ||
            message.Body.Length == 0)
            return;

        e.Handled = true;
        if (ReferenceEquals(_simToggled, message) && Environment.TickCount64 - _simToggledTick < 600)
            vm.ToggleSimTag(message);
        _simToggled = null;

        var index = Math.Clamp(block.TextLayout.HitTestPoint(e.GetPosition(block)).TextPosition, 0, message.Body.Length - 1);
        var (start, end) = WordAt(message, index);
        _selectionPointer = e.Pointer;
        OpenTextSelection(block, message, start, end);
    }

    // The text block of the bubble the element is in.
    private TextBlock? BodyOf(Visual? source)
    {
        for (var visual = source; visual is not null && !ReferenceEquals(visual, MessagesList); visual = visual.GetVisualParent())
        {
            if (visual is Control { Tag: "bubble" } bubble)
                return bubble.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(block => block.Classes.Contains("body"));
        }

        return null;
    }

    /// <summary>A number, link or phone number whole; otherwise the word under the finger, or the character when it is none (an emoji, a sign).</summary>
    private static (int Start, int End) WordAt(ChatMessage message, int index)
    {
        foreach (var span in message.Links ?? [])
        {
            if (index >= span.Start && index < span.End)
                return (span.Start, span.End);
        }

        var text = message.Body;
        if (!IsWordChar(text[index]) && index > 0 && IsWordChar(text[index - 1]))
            index--;

        if (!IsWordChar(text[index]))
        {
            if (char.IsLowSurrogate(text[index]) && index > 0)
                index--;
            return (index, Math.Min(text.Length, index + (char.IsHighSurrogate(text[index]) ? 2 : 1)));
        }

        var start = index;
        while (start > 0 && IsWordChar(text[start - 1]))
            start--;

        var end = index;
        while (end < text.Length && IsWordChar(text[end]))
            end++;

        return (start, end);
    }

    // Persian words keep their zero-width non-joiner ("می‌شود") and their vowel marks.
    private static bool IsWordChar(char c) =>
        char.IsLetterOrDigit(c) || c == ZeroWidthNonJoiner ||
        char.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.ConnectorPunctuation;

    private void OpenTextSelection(TextBlock block, ChatMessage message, int start, int end)
    {
        CloseTextSelection();
        _openingSelection = true;
        try
        {
            OpenTextSelectionCore(block, message, start, end);
        }
        finally
        {
            _openingSelection = false;
        }
    }

    private void OpenTextSelectionCore(TextBlock block, ChatMessage message, int start, int end)
    {
        var box = SelectionBox;
        _selectedBlock = block;
        _selectedMessage = message;

        box.Text = message.Body;
        // Laid out like the block, so the selection falls on its letters.
        box.FontFamily = block.FontFamily;
        box.FontSize = block.FontSize;
        box.FontWeight = block.FontWeight;
        box.FontStyle = block.FontStyle;
        box.LineHeight = block.LineHeight;
        box.LetterSpacing = block.LetterSpacing;
        box.FlowDirection = block.FlowDirection;
        box.TextAlignment = block.TextAlignment;
        box.SelectionBrush = message.IsOutgoing ? OutgoingSelection : IncomingSelection;
        box.IsVisible = true;
        if (!PlaceSelectionBox())
            return;

        box.UpdateLayout();
        box.Focus();
        box.SelectionStart = start;
        box.SelectionEnd = end;
        ShowSelectionHandles(box);
        TriggerHaptic(global::Android.Views.FeedbackConstants.LongPress);

        _ = ShowSelectionContactAsync(message.Body[start..end], settle: false);
        TextMenu.Show(box);
    }

    // The box only puts up its handles after a touch on it; this selection was made on the bubble, so it is told of that touch.
    private void ShowSelectionHandles(TextBox box)
    {
        if (_lastPress is { Pointer.Type: PointerType.Touch } press &&
            box.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault() is { } presenter)
            presenter.RaiseEvent(new TappedEventArgs(TappedEvent, press));
    }

    /// <summary>Keeps the box on the block as the list moves; false (and closed) when the row was scrolled away and reused.</summary>
    private bool PlaceSelectionBox()
    {
        if (_selectedBlock is not { } block)
            return false;

        if (!ReferenceEquals(block.DataContext, _selectedMessage) || !block.IsEffectivelyVisible ||
            block.TranslatePoint(default, SelectionLayer) is not { } a ||
            block.TranslatePoint(new Point(block.Bounds.Width, block.Bounds.Height), SelectionLayer) is not { } b)
        {
            CloseTextSelection();
            return false;
        }

        // Corner to corner: a block in the other direction than the page is mirrored inside its own bounds.
        Canvas.SetLeft(SelectionBox, Math.Min(a.X, b.X));
        Canvas.SetTop(SelectionBox, Math.Min(a.Y, b.Y));
        // A hair wider than the block, so rounding never wraps a line the block keeps whole. The height follows the text.
        SelectionBox.Width = block.Bounds.Width + 1;
        return true;
    }

    private void CloseTextSelection()
    {
        if (!IsTextSelected)
            return;

        _selectedBlock = null;
        _selectedMessage = null;
        _contactVersion++;
        TextMenu.SetFooter(SelectionBox, null);
        TextMenu.Close(SelectionBox);
        // The box gives up focus first: that takes its handles down with it.
        if (SelectionBox.IsFocused)
            SelectionLayer.Focus();
        SelectionBox.IsVisible = false;
    }

    // The contact under the bar follows the selection; while a handle is dragged it waits for the selection to settle.
    private async Task ShowSelectionContactAsync(string selected, bool settle)
    {
        var version = ++_contactVersion;
        TextMenu.SetFooter(SelectionBox, null);
        if (PhoneNumberNormalizer.ToContactKey(selected) is null)
            return;

        if (settle)
            await Task.Delay(300);
        if (version != _contactVersion || await FindContactAsync(selected) is not { } name ||
            version != _contactVersion || !IsTextSelected)
            return;

        if (_selectionContact is not { } chip)
        {
            _selectionContact = chip = new ContactChip { Margin = new Thickness(0, 3, 0, 0) };
            chip.Tapped += (_, e) =>
            {
                e.Handled = true;
                var number = chip.Number;
                CloseTextSelection();
                TriggerHaptic();
                ConversationRequested?.Invoke(number);
            };
        }

        chip.Show(name, selected.Trim());
        TextMenu.SetFooter(SelectionBox, chip);
    }

    // A tapped phone number gets its contact on the link card the same way.
    private async Task ShowLinkContactAsync(string shown, string target)
    {
        if (await FindContactAsync(shown) is { } name && LinkPopup.IsOpen && LinkPopup.Target == target)
            LinkPopup.ShowContact(name, shown);
    }

    /// <summary>Whose a number picked out of a message is, matched the way the inbox names its senders; null for a code, an amount, or a number nobody saved.</summary>
    private static async Task<string?> FindContactAsync(string number)
    {
        if (PhoneNumberNormalizer.ToContactKey(number) is null ||
            IPlatformApplication.Current?.Services.GetService<ISmsService>() is not { } sms)
            return null;

        try
        {
            return await sms.FindContactNameAsync(number);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Contact lookup for a number in a message failed: {ex}");
            return null;
        }
    }
}
