using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Gheychi.App.Ui;

namespace Gheychi.App.Controls;

// The emoji panel: it takes the place of the keyboard under the message box. The page is lifted by the keyboard (see
// MainView.ApplyInsets), so swapping one for the other must change the box's room by nothing: the panel is given the
// height the keyboard had, and the one that is going away is only removed when the other has arrived (the input pane
// reports it), so both changes land in the same layout pass.
public partial class ChatView
{
    // Used until a keyboard has been seen; the same on every chat, so it outlives them.
    private const double DefaultKeyboardHeight = 300;
    private static readonly TimeSpan SwapGrace = TimeSpan.FromMilliseconds(450);

    private static double _keyboardHeight;

    private EmojiPanel? _emojiPanel;
    private IInputPane? _inputPane;
    private bool _emojiOpen;
    private bool _emojiWaitsForKeyboardDown;
    private bool _emojiWaitsForKeyboardUp;
    private int _swap;
    private double _navigationInset;

    /// <summary>True while the emoji panel is showing.</summary>
    public bool IsEmojiOpen => _emojiOpen;

    private void InitEmoji()
    {
        // A tap on the box while the panel is up brings the keyboard back in its place.
        MessageEntry.AddHandler(PointerPressedEvent, (_, _) =>
        {
            if (_emojiOpen)
                CloseEmojiPanel(showKeyboard: true);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevel.GetTopLevel(this)?.InputPane is { } pane && !ReferenceEquals(pane, _inputPane))
        {
            _inputPane = pane;
            pane.StateChanged += OnInputPaneChanged;
            NoteKeyboard();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_inputPane is { } pane)
            pane.StateChanged -= OnInputPaneChanged;
        _inputPane = null;
        base.OnDetachedFromVisualTree(e);
    }

    // Remembers how tall the keyboard is whenever it is up, and settles a swap that was waiting for it.
    private void OnInputPaneChanged(object? sender, EventArgs e)
    {
        NoteKeyboard();

        if (_emojiWaitsForKeyboardDown && _inputPane is { State: InputPaneState.Closed })
        {
            _emojiWaitsForKeyboardDown = false;
            ShowEmojiHost();
        }
        else if (_emojiWaitsForKeyboardUp && _inputPane is { State: InputPaneState.Open })
        {
            _emojiWaitsForKeyboardUp = false;
            HideEmojiHost();
        }
    }

    private void NoteKeyboard()
    {
        if (_inputPane is not { } pane || TopLevel.GetTopLevel(this) is not { } top)
            return;

        if (pane.State == InputPaneState.Open)
        {
            // The same reading MainView lifts the page by.
            var height = top.Bounds.Height - pane.OccludedRect.Top;
            if (pane.OccludedRect.Height > 0 && height > 0 && height <= top.Bounds.Height * 0.6)
                _keyboardHeight = height;
        }
        else if (top.InsetsManager is { DisplaysEdgeToEdge: true } insets)
            _navigationInset = insets.SafeAreaPadding.Bottom;
    }

    private void OnEmojiTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
        if (_emojiOpen)
            CloseEmojiPanel(showKeyboard: true);
        else
            OpenEmojiPanel();
    }

    private void OpenEmojiPanel()
    {
        if (_emojiOpen)
            return;

        if (_emojiPanel is null)
        {
            _emojiPanel = new EmojiPanel();
            _emojiPanel.EmojiPicked += emoji => EmojiPanel.Insert(MessageEntry, emoji);
            _emojiPanel.BackspaceRequested += () => EmojiPanel.Delete(MessageEntry);
            EmojiPanelHost.Children.Add(_emojiPanel);
        }

        _emojiPanel.Reset();
        _emojiOpen = true;
        _swap++;
        var keyboardWasComing = _emojiWaitsForKeyboardUp;
        _emojiWaitsForKeyboardUp = false;
        EmojiButtonIcon.Data = EmojiIcon("Icon.Keyboard");

        // An unfocused box may hold a caret from an earlier text (another chat, a draft, a sent message): the emoji go after what is written.
        if (!MessageEntry.IsFocused)
            MessageEntry.CaretIndex = MessageEntry.Text?.Length ?? 0;

        if (_inputPane is { State: InputPaneState.Open })
        {
            // The panel waits for the keyboard to be gone: shown first, the box would be lifted by both.
            _emojiWaitsForKeyboardDown = true;
            SetKeyboard(false);
            Fallback(() => _emojiWaitsForKeyboardDown, () =>
            {
                _emojiWaitsForKeyboardDown = false;
                ShowEmojiHost();
            });
        }
        else
        {
            // A keyboard asked for by the close that was cancelled has not arrived yet: it is called off, the panel never left.
            if (keyboardWasComing)
                SetKeyboard(false);
            ShowEmojiHost();
        }
    }

    /// <summary>Takes the panel away; with <paramref name="showKeyboard"/> the keyboard comes up in its place.</summary>
    public void CloseEmojiPanel(bool showKeyboard = false)
    {
        if (!_emojiOpen)
            return;

        _emojiOpen = false;
        _swap++;
        _emojiWaitsForKeyboardDown = false;
        EmojiButtonIcon.Data = EmojiIcon("Icon.Emoji");

        if (!showKeyboard)
        {
            HideEmojiHost();
            return;
        }

        // The panel stays until the keyboard has arrived.
        _emojiWaitsForKeyboardUp = true;
        MessageEntry.Focus();
        SetKeyboard(true);
        Fallback(() => _emojiWaitsForKeyboardUp, () =>
        {
            _emojiWaitsForKeyboardUp = false;
            HideEmojiHost();
        });
    }

    private void ShowEmojiHost()
    {
        if (!_emojiOpen)
            return;

        var keyboard = _keyboardHeight > 0 ? _keyboardHeight : DefaultKeyboardHeight;
        EmojiPanelHost.Height = Math.Max(0, keyboard - _navigationInset);
        EmojiPanelHost.IsVisible = true;
    }

    private void HideEmojiHost()
    {
        if (_emojiOpen)
            return;

        EmojiPanelHost.IsVisible = false;
    }

    // The input pane may never report (a keyboard that was not really up or down): the swap is finished anyway.
    // A timer only ends the swap that started it: a later open or close has moved on.
    private void Fallback(Func<bool> stillWaiting, Action finish)
    {
        var swap = _swap;
        DispatcherTimer.RunOnce(() =>
        {
            if (swap == _swap && stillWaiting())
                finish();
        }, SwapGrace);
    }

    private Avalonia.Media.Geometry? EmojiIcon(string key) => this.FindResource(key) as Avalonia.Media.Geometry;

    private static void SetKeyboard(bool show)
    {
        if (Platform.CurrentActivity is not { } activity ||
            activity.GetSystemService(global::Android.Content.Context.InputMethodService) is not global::Android.Views.InputMethods.InputMethodManager manager)
            return;

        var view = activity.CurrentFocus ?? activity.Window?.DecorView;
        if (view is null)
            return;

        if (show)
            manager.ShowSoftInput(view, (global::Android.Views.InputMethods.ShowFlags)0);
        else
            manager.HideSoftInputFromWindow(view.WindowToken, global::Android.Views.InputMethods.HideSoftInputFlags.None);
    }
}
