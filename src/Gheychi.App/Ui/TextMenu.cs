using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using Gheychi.App.Localization;

namespace Gheychi.App.Ui;

/// <summary>
/// The cut / copy / paste bar of a text box, in the app's own look in place of the theme's desktop menu. Switched on for
/// every text box by a style (<c>ui:TextMenu.Enabled</c>). It sits above the selected text, or the caret when nothing is
/// selected, and is only moved below it when there is no room above.
/// </summary>
public static class TextMenu
{
    private static readonly TimeSpan FadeIn = TimeSpan.FromMilliseconds(120);

    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<TextBox, TextBox, bool>("Enabled");

    private static readonly ConditionalWeakTable<TextBox, Bar> Bars = new();

    static TextMenu()
    {
        EnabledProperty.Changed.AddClassHandler<TextBox>((box, e) =>
        {
            if (e.GetNewValue<bool>() && !Bars.TryGetValue(box, out _))
                Bars.Add(box, new Bar(box));
        });
    }

    public static bool GetEnabled(TextBox box) => box.GetValue(EnabledProperty);

    public static void SetEnabled(TextBox box, bool value) => box.SetValue(EnabledProperty, value);

    public static bool IsOpen(TextBox box) => Bars.TryGetValue(box, out var bar) && bar.IsOpen;

    public static void Close(TextBox box)
    {
        if (Bars.TryGetValue(box, out var bar))
            bar.Hide();
    }

    private sealed class Bar
    {
        private readonly TextBox _box;
        private Border _card = null!;
        private Popup _popup = null!;
        private Button _cut = null!, _copy = null!, _paste = null!, _all = null!;

        public Bar(TextBox box)
        {
            _box = box;

            // The theme's own menu is replaced: its popup opens beside the finger, not above the text.
            box.ContextFlyout = null;
            box.ContextRequested += (_, e) =>
            {
                e.Handled = true;
                Show();
            };
            box.LostFocus += (_, _) => Hide();
            box.PropertyChanged += (_, e) =>
            {
                if (e.Property == TextBox.SelectionStartProperty || e.Property == TextBox.SelectionEndProperty || e.Property == TextBox.TextProperty)
                    Hide();
            };
        }

        public bool IsOpen => _popup is { IsOpen: true };

        // Built on first use: most boxes are never long-pressed.
        private void Build()
        {
            var box = _box;
            var loc = LocalizationManager.Instance;
            _cut = Item(loc["TextMenu_Cut"], () => box.Cut());
            _copy = Item(loc["TextMenu_Copy"], () => { box.Copy(); box.ClearSelection(); });
            _paste = Item(loc["TextMenu_Paste"], () => box.Paste());
            _all = Item(loc["TextMenu_SelectAll"], () => box.SelectAll());

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            row.Children.AddRange([_cut, _copy, _paste, _all]);

            _card = new Border
            {
                Background = Palette.Pick("#FFFFFF", "#272B33"),
                BorderBrush = Palette.Pick("#E2E8DF", "#363B44"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(4),
                // Room for the shadow, which the popup would otherwise clip.
                Margin = new Thickness(12),
                BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 3, Blur = 14, Color = Color.FromArgb(0x38, 0, 0, 0) }),
                Child = row
            };

            _popup = new Popup
            {
                Child = _card,
                Placement = PlacementMode.Top,
                PlacementTarget = box,
                PlacementConstraintAdjustment = PopupPositionerConstraintAdjustment.SlideX | PopupPositionerConstraintAdjustment.FlipY,
                IsLightDismissEnabled = true,
                OverlayDismissEventPassThrough = true,
                WindowManagerAddShadowHint = false
            };
            _popup.Opened += (_, _) => _ = OverlayAnimator.FadeAsync(_card, 0, 1, FadeIn);
        }

        public void Hide()
        {
            if (!IsOpen)
                return;

            _popup.IsOpen = false;
            ((ISetLogicalParent)_popup).SetParent(null);
        }

        private void Show()
        {
            if (_popup is null)
                Build();

            _cut.IsVisible = _box.CanCut;
            _copy.IsVisible = _box.CanCopy;
            _paste.IsVisible = _box.CanPaste;
            _all.IsVisible = !string.IsNullOrEmpty(_box.Text) && _box.SelectedText?.Length != _box.Text.Length;
            if (!_cut.IsVisible && !_copy.IsVisible && !_paste.IsVisible && !_all.IsVisible)
                return;

            _popup.PlacementRect = AnchorRect();
            _card.Opacity = 0;
            if (!_popup.IsOpen)
            {
                ((ISetLogicalParent)_popup).SetParent(_box);
                _popup.IsOpen = true;
            }
        }

        // The selection (or the caret) in the box's own coordinates, kept inside the box: a long message scrolls, and
        // a selection that has gone out of view would send the menu off screen.
        private Rect AnchorRect()
        {
            var inside = new Rect(_box.Bounds.Size);
            var presenter = _box.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault();
            if (presenter?.TextLayout is not { } layout)
                return inside;

            var start = Math.Min(_box.SelectionStart, _box.SelectionEnd);
            var length = Math.Abs(_box.SelectionEnd - _box.SelectionStart);
            Rect? union = null;
            foreach (var rect in length > 0 ? layout.HitTestTextRange(start, length) : [layout.HitTestTextPosition(_box.CaretIndex)])
                union = union is { } u ? u.Union(rect) : rect;

            if (union is not { } text || presenter.TranslatePoint(text.TopLeft, _box) is not { } topLeft ||
                presenter.TranslatePoint(text.BottomRight, _box) is not { } bottomRight)
                return inside;

            var left = Math.Clamp(topLeft.X, 0, inside.Width);
            var right = Math.Clamp(bottomRight.X, left, inside.Width);
            var top = Math.Clamp(topLeft.Y, 0, inside.Height);
            var bottom = Math.Clamp(bottomRight.Y, top, inside.Height);
            return new Rect(left, top, right - left, bottom - top);
        }

        private Button Item(string text, Action action)
        {
            var button = new Button { Content = text, Focusable = false };
            button.Classes.Add("textmenu");
            button.Click += (_, _) =>
            {
                Hide();
                action();
            };
            return button;
        }
    }
}
