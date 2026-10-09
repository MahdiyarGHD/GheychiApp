using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Gheychi.App.Localization;
using Gheychi.App.Theming;
using Gheychi.App.Ui;
using Gheychi.Core.Services;

namespace Gheychi.App.Controls;

/// <summary>
/// The small card that opens on a tapped link or phone number: open or call, and copy. It fills the page it is
/// placed on (a tap outside the card closes it) and puts the card next to the text that was tapped.
/// </summary>
public partial class LinkMenu : UserControl
{
    private const double Edge = 8;
    private const double Gap = 6;
    private static readonly TimeSpan FadeIn = TimeSpan.FromMilliseconds(120);

    private readonly IBrush _pressed = Palette.Pick("#14000000", "#1AFFFFFF");
    private LinkKind _kind;
    private string _target = string.Empty;

    public LinkMenu()
    {
        InitializeComponent();
        foreach (var row in new[] { PrimaryRow, CopyRow })
            TrackPress(row);
    }

    public bool IsOpen => IsVisible;

    /// <param name="anchor">The tapped text, in the coordinates of this control.</param>
    /// <param name="shown">The text as it is written in the message.</param>
    /// <param name="target">What the actions use: the address with its scheme, or the number as digits.</param>
    public void Show(Rect anchor, LinkKind kind, string shown, string target)
    {
        _kind = kind;
        _target = target;

        var loc = LocalizationManager.Instance;
        Subject.Text = shown;
        if (kind == LinkKind.Url)
        {
            PrimaryIcon.Data = this.FindResource("Icon.OpenInNew") as Geometry;
            PrimaryLabel.Text = loc["Chat_OpenInBrowser"];
            CopyLabel.Text = loc["Chat_CopyLink"];
        }
        else
        {
            PrimaryIcon.Data = this.FindResource("Icon.Phone") as Geometry;
            PrimaryLabel.Text = loc["Chat_Call"];
            CopyLabel.Text = loc["Chat_Copy"];
        }

        // Measured where it will be shown, so the card is put beside the text without guessing its size.
        Opacity = 0;
        Card.Margin = default;
        IsVisible = true;
        UpdateLayout();

        var size = Card.Bounds.Size;
        var x = Math.Clamp(anchor.Left, Edge, Math.Max(Edge, Bounds.Width - size.Width - Edge));
        var below = anchor.Bottom + Gap;
        var y = below + size.Height <= Bounds.Height - Edge
            ? below
            : Math.Max(Edge, anchor.Top - Gap - size.Height);
        Card.Margin = new Thickness(x, y, 0, 0);

        _ = OverlayAnimator.FadeAsync(this, 0, 1, FadeIn);
    }

    public void Hide()
    {
        IsVisible = false;
        Opacity = 1;
    }

    private void TrackPress(Border row)
    {
        void Release(object? sender, RoutedEventArgs e) => row.Background = Brushes.Transparent;
        row.PointerPressed += (_, _) => row.Background = _pressed;
        row.PointerReleased += Release;
        row.PointerExited += Release;
        row.PointerCaptureLost += (_, _) => row.Background = Brushes.Transparent;
    }

    private void OnScrimTapped(object? sender, TappedEventArgs e) => Hide();

    // A tap inside the card belongs to the card: it must not reach the scrim behind it.
    private void OnCardTapped(object? sender, TappedEventArgs e) => e.Handled = true;

    private async void OnPrimaryTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
        var kind = _kind;
        var target = _target;
        Hide();
        ChatView.TriggerHaptic();

        try
        {
            if (kind == LinkKind.Phone)
                Platforms.Android.ProfileLauncher.Dial(target);
            else if (!await Launcher.Default.OpenAsync(target))
                await Dialogs.AlertAsync(string.Empty, LocalizationManager.Instance["Chat_LinkOpenFailed"], "OK");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Opening a link failed: {ex}");
        }
    }

    private async void OnCopyTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
        var target = _target;
        Hide();
        ChatView.TriggerHaptic();

        try
        {
            await Clipboard.Default.SetTextAsync(target);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Copying a link failed: {ex}");
        }
    }
}
