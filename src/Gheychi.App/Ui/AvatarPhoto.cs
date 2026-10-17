using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Gheychi.App.Platforms.Android.Services;

namespace Gheychi.App.Ui;

/// <summary>
/// <c>ui:AvatarPhoto.Address="{Binding PhotoAddress}"</c> on the round avatar of a person: the contact's picture is drawn
/// over the initials, when there is one. Rows are reused as a list scrolls, so a new address first clears the old picture.
/// </summary>
public static class AvatarPhoto
{
    public static readonly AttachedProperty<string?> AddressProperty =
        AvaloniaProperty.RegisterAttached<Border, Border, string?>("Address", null);

    /// <summary>True while something else is drawn over the avatar (the tick of a selected row).</summary>
    public static readonly AttachedProperty<bool> HiddenProperty =
        AvaloniaProperty.RegisterAttached<Border, Border, bool>("Hidden", false);

    /// <summary>A big avatar (the profile): the picture is read at a larger size.</summary>
    public static readonly AttachedProperty<bool> LargeProperty =
        AvaloniaProperty.RegisterAttached<Border, Border, bool>("Large", false);

    private static readonly AttachedProperty<Ellipse?> OverlayProperty =
        AvaloniaProperty.RegisterAttached<Border, Border, Ellipse?>("Overlay", null);

    static AvatarPhoto()
    {
        AddressProperty.Changed.AddClassHandler<Border>((border, _) => Refresh(border));
        HiddenProperty.Changed.AddClassHandler<Border>((border, _) => ApplyVisibility(border));
    }

    public static string? GetAddress(Border border) => border.GetValue(AddressProperty);

    public static void SetAddress(Border border, string? value) => border.SetValue(AddressProperty, value);

    public static bool GetHidden(Border border) => border.GetValue(HiddenProperty);

    public static void SetHidden(Border border, bool value) => border.SetValue(HiddenProperty, value);

    public static bool GetLarge(Border border) => border.GetValue(LargeProperty);

    public static void SetLarge(Border border, bool value) => border.SetValue(LargeProperty, value);

    private static async void Refresh(Border border)
    {
        try
        {
            if (border.GetValue(OverlayProperty) is { } shown)
            {
                shown.Tag = null;
                ApplyVisibility(border);
            }

            var address = GetAddress(border);
            if (string.IsNullOrEmpty(address))
                return;

            var task = ContactPhotos.GetAsync(address, GetLarge(border));
            var bitmap = task.IsCompletedSuccessfully ? task.Result : await task;
            if (bitmap is null || GetAddress(border) != address)
                return;

            Show(border, bitmap);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Avatar photo failed: {ex}");
        }
    }

    private static void Show(Border border, Bitmap bitmap)
    {
        var overlay = border.GetValue(OverlayProperty);
        if (overlay is null)
        {
            if (border.Child is not Panel panel)
            {
                var inner = border.Child;
                border.Child = null;
                panel = new Panel();
                if (inner is not null)
                    panel.Children.Add(inner);
                border.Child = panel;
            }

            overlay = new Ellipse { IsHitTestVisible = false };
            panel.Children.Add(overlay);
            border.SetValue(OverlayProperty, overlay);
        }

        overlay.Fill = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
        overlay.Tag = true;
        ApplyVisibility(border);
    }

    private static void ApplyVisibility(Border border)
    {
        if (border.GetValue(OverlayProperty) is { } overlay)
            overlay.IsVisible = overlay.Tag is true && !GetHidden(border);
    }
}
