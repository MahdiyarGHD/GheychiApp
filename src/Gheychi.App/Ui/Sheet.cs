using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Gheychi.App.Theming;

namespace Gheychi.App.Ui;

/// <summary>
/// The app's own sheets and dialogs, drawn in the app's colours and fonts and slid in on the compositor, in place of the
/// system's dialogs. A bottom sheet for lists and pickers, a centred card for questions and text.
/// </summary>
public static class Sheet
{
    private static readonly List<Action> Dismissers = [];

    public static bool IsOpen => Dismissers.Count > 0;

    /// <summary>The system back button closes the topmost sheet; true when there was one.</summary>
    public static bool HandleBack()
    {
        if (Dismissers.Count == 0)
            return false;

        Dismissers[^1]();
        return true;
    }

    // ---- Look ------------------------------------------------------------------------------------

    public static IBrush Ink => Palette.Pick("#1B1E24", "#E8EAED");

    public static IBrush Soft => Palette.Pick("#5F6368", "#B0B5BC");

    public static IBrush Muted => Palette.Brush("#8A8F98");

    public static IBrush Surface => Palette.Pick("#FFFFFF", "#1C1F24");

    public static IBrush Field => Palette.Pick("#EEF0F2", "#262A31");

    public static IBrush Line => Palette.Pick("#E3E5E8", "#2A2E35");

    public static IBrush Danger => Palette.Brush("#EF4444");

    public static IBrush Accent => Palette.Accent(AccentRole.Text);

    public static TextBlock Label(string text, double size, IBrush brush, bool bold = false) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = brush,
        FontFamily = bold ? AppFonts.Bold : AppFonts.Regular,
        TextWrapping = TextWrapping.Wrap
    };

    public static Control Grabber() => new Border
    {
        Width = 36,
        Height = 4,
        CornerRadius = new CornerRadius(2),
        Margin = new Thickness(0, 10, 0, 6),
        HorizontalAlignment = HorizontalAlignment.Center,
        Background = Palette.Pick("#D5D8DC", "#3A3F47")
    };

    public static Control Heading(string text) => new TextBlock
    {
        Text = text,
        FontSize = 13,
        FontFamily = AppFonts.Bold,
        Foreground = Muted,
        Margin = new Thickness(22, 6, 22, 8),
        TextTrimming = TextTrimming.CharacterEllipsis,
        MaxLines = 1
    };

    public static Control Divider() => new Border { Height = 1, Background = Line };

    /// <summary>A row that dims under the finger and reports a tap.</summary>
    public static Border Tappable(Control content, Action tap, Thickness? padding = null, double minHeight = 52)
    {
        var row = new Border
        {
            MinHeight = minHeight,
            Padding = padding ?? new Thickness(22, 8),
            Background = Palette.Transparent,
            Child = content
        };
        content.VerticalAlignment = VerticalAlignment.Center;

        void Rest(object? s, EventArgs e) => row.Background = Palette.Transparent;
        row.PointerPressed += (_, _) => row.Background = Palette.Accent(AccentRole.Soft, 0x66);
        row.PointerReleased += Rest;
        row.PointerCaptureLost += Rest;
        row.Tapped += (_, e) =>
        {
            e.Handled = true;
            tap();
        };
        return row;
    }

    public static Border TextRow(string text, IBrush brush, Action tap, bool bold = false) =>
        Tappable(Label(text, 16, brush, bold), tap);

    /// <summary>A pill: filled with the accent for the main action, bare text for the others.</summary>
    public static Border Button(string text, bool filled, Action tap, IBrush? textBrush = null)
    {
        var label = Label(text, 15, filled ? Palette.Brush("#FFFFFF") : textBrush ?? Accent, bold: true);
        label.TextWrapping = TextWrapping.NoWrap;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;

        var button = new Border
        {
            MinHeight = 44,
            MinWidth = 72,
            Padding = new Thickness(20, 0),
            CornerRadius = new CornerRadius(22),
            Background = filled ? Palette.Accent(AccentRole.Solid) : Palette.Transparent,
            Child = label
        };

        void Rest(object? s, EventArgs e) => button.Opacity = 1;
        button.PointerPressed += (_, _) => button.Opacity = 0.7;
        button.PointerReleased += Rest;
        button.PointerCaptureLost += Rest;
        button.Tapped += (_, e) =>
        {
            e.Handled = true;
            if (button.Opacity > 0.5)
                tap();
        };
        return button;
    }

    // ---- Hosting ---------------------------------------------------------------------------------

    /// <summary>
    /// Shows what <paramref name="build"/> makes and completes with the value passed to the callback it is given, or with
    /// <paramref name="dismissed"/> when the user taps outside or goes back.
    /// </summary>
    public static async Task<T> ShowAsync<T>(Func<Action<T>, Control> build, T dismissed, bool centered = false)
    {
        if (MainView.Current is not { } main)
            return dismissed;

        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var closing = false;
        Action dismiss = () => { };
        Border card = null!;
        Border scrim = null!;
        Panel root = null!;

        void Finish(T value)
        {
            if (closing)
                return;

            closing = true;
            Dismissers.Remove(dismiss);
            done.TrySetResult(value);
            _ = CloseAsync();
        }

        dismiss = () => Finish(dismissed);

        scrim = new Border { Background = Palette.Brush("#8C000000"), Opacity = 0 };
        scrim.Tapped += (_, _) => Finish(dismissed);

        card = new Border
        {
            Opacity = 0,
            Background = Surface,
            BorderBrush = Line,
            BorderThickness = centered ? new Thickness(1) : new Thickness(0, 1, 0, 0),
            CornerRadius = centered ? new CornerRadius(24) : new CornerRadius(24, 24, 0, 0),
            VerticalAlignment = centered ? VerticalAlignment.Center : VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = centered ? new Thickness(24, 24, 24, 24) : default,
            MaxWidth = centered ? 360 : 560,
            BoxShadow = BoxShadows.Parse("0 8 28 0 #55000000"),
            Child = build(Finish)
        };
        if (!centered)
            card.HorizontalAlignment = HorizontalAlignment.Stretch;
        card.Tapped += (_, e) => e.Handled = true;

        root = new Panel { ZIndex = 1000, Children = { scrim, card } };
        Dismissers.Add(dismiss);
        main.Overlays.Children.Add(root);
        _ = OpenAsync();
        return await done.Task;

        async Task OpenAsync()
        {
            await OverlayAnimator.SettleAsync();
            if (closing)
                return;

            var travel = centered ? 20 : Math.Max(card.Bounds.Height, 200);
            OverlayAnimator.SetTranslation(card, 0, travel);
            if (!centered)
                card.Opacity = 1;

            await Task.WhenAll(
                OverlayAnimator.FadeAsync(scrim, 0, 1, OverlayAnimator.OpenDuration),
                OverlayAnimator.SlideYAsync(card, travel, 0, OverlayAnimator.OpenDuration, decelerate: true),
                centered ? OverlayAnimator.FadeAsync(card, 0, 1, OverlayAnimator.OpenDuration) : Task.CompletedTask);
        }

        async Task CloseAsync()
        {
            var travel = centered ? 12 : Math.Max(card.Bounds.Height, 200);
            await Task.WhenAll(
                OverlayAnimator.FadeAsync(scrim, scrim.Opacity, 0, OverlayAnimator.CloseDuration),
                centered
                    ? OverlayAnimator.FadeAsync(card, card.Opacity, 0, OverlayAnimator.CloseDuration)
                    : OverlayAnimator.SlideYAsync(card, 0, travel, OverlayAnimator.CloseDuration, decelerate: false));
            main.Overlays.Children.Remove(root);
        }
    }

    // ---- Ready-made ------------------------------------------------------------------------------

    /// <summary>The chosen item's text, <paramref name="cancel"/> when the user backed out. A destructive item comes first, in red.</summary>
    public static Task<string?> ListAsync(string? title, string cancel, string? destruction, string[] buttons)
    {
        var items = new List<string>(buttons);
        if (!string.IsNullOrEmpty(destruction))
            items.Insert(0, destruction);

        return ShowAsync<string?>(finish =>
        {
            var rows = new StackPanel();
            foreach (var item in items)
            {
                var text = item;
                var isDestructive = !string.IsNullOrEmpty(destruction) && text == destruction;
                rows.Children.Add(TextRow(text, isDestructive ? Danger : Ink, () => finish(text)));
            }

            var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            stack.Children.Add(Grabber());
            if (!string.IsNullOrEmpty(title))
                stack.Children.Add(Heading(title));
            stack.Children.Add(new ScrollViewer
            {
                MaxHeight = MaxBodyHeight(),
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
                Content = rows
            });
            stack.Children.Add(Divider());
            stack.Children.Add(TextRow(cancel, Muted, () => finish(cancel), bold: true));
            return stack;
        }, cancel);
    }

    /// <summary>True when the user chose <paramref name="accept"/>. Without a <paramref name="cancel"/> text it is a plain notice.</summary>
    public static Task<bool> AlertAsync(string title, string message, string accept, string? cancel, bool danger)
    {
        return ShowAsync<bool>(finish =>
        {
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
            if (!string.IsNullOrEmpty(cancel))
                buttons.Children.Add(Button(cancel, filled: false, () => finish(false), Muted));
            buttons.Children.Add(Button(accept, filled: false, () => finish(true), danger ? Danger : Accent));

            var body = new StackPanel { Margin = new Thickness(24, 22, 18, 14), Spacing = 10 };
            body.Children.Add(Label(title, 18, Ink, bold: true));
            body.Children.Add(Label(message, 14.5, Soft));
            body.Children.Add(new Border { Height = 4 });
            body.Children.Add(buttons);
            return body;
        }, false, centered: true);
    }

    /// <summary>The text typed, or null when the user cancelled (or, when <paramref name="required"/>, never wrote any).</summary>
    public static Task<string?> PromptAsync(
        string title, string? message, string initial, string? hint, string accept, string cancel,
        int maxLength = 0, bool multiline = false, bool required = true)
    {
        return ShowAsync<string?>(finish =>
        {
            var box = new TextBox
            {
                Text = initial,
                PlaceholderText = hint,
                PlaceholderForeground = Muted,
                Foreground = Ink,
                FontSize = 16,
                FontFamily = AppFonts.Regular,
                AcceptsReturn = multiline,
                TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
                MaxHeight = multiline ? 190 : double.PositiveInfinity,
                MinHeight = multiline ? 90 : 0
            };
            box.Classes.Add("plain");
            if (maxLength > 0)
                box.MaxLength = maxLength;
            ScrollViewer.SetVerticalScrollBarVisibility(box, Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden);

            var field = new Border
            {
                Background = Field,
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(14, multiline ? 6 : 4),
                Child = box
            };

            Border? save = null;
            void Submit()
            {
                var text = box.Text ?? string.Empty;
                if (required && string.IsNullOrWhiteSpace(text))
                    return;

                finish(text);
            }

            save = Button(accept, filled: true, Submit);
            void Sync() => save.Opacity = required && string.IsNullOrWhiteSpace(box.Text) ? 0.4 : 1;
            box.TextChanged += (_, _) => Sync();
            Sync();

            if (!multiline)
            {
                box.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Enter)
                    {
                        e.Handled = true;
                        Submit();
                    }
                };
            }

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(Button(cancel, filled: false, () => finish(null), Muted));
            buttons.Children.Add(save);

            var body = new StackPanel { Margin = new Thickness(24, 22, 18, 16), Spacing = 12 };
            body.Children.Add(Label(title, 18, Ink, bold: true));
            if (!string.IsNullOrEmpty(message))
                body.Children.Add(Label(message, 14, Soft));
            body.Children.Add(field);
            body.Children.Add(buttons);

            DispatcherTimer.RunOnce(() =>
            {
                box.Focus();
                box.CaretIndex = box.Text?.Length ?? 0;
            }, TimeSpan.FromMilliseconds(260));
            return body;
        }, null, centered: true);
    }

    /// <summary>The most a scrolling body may take: what is left of the screen under the sheet's own header and buttons.</summary>
    public static double MaxBodyHeight() =>
        (TopLevel.GetTopLevel(MainView.Current)?.Bounds.Height ?? 800) * 0.55;
}
