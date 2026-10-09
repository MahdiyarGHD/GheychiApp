using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;
using Gheychi.App.Localization;

namespace Gheychi.App.Ui;

/// <summary>
/// The cut / copy / paste bar of a text box, in the app's own look in place of the theme's desktop menu. Switched on for
/// every text box by a style (<c>ui:TextMenu.Enabled</c>); each box gets its own flyout when it is created.
/// </summary>
public static class TextMenu
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<TextBox, TextBox, bool>("Enabled");

    static TextMenu()
    {
        EnabledProperty.Changed.AddClassHandler<TextBox>((box, e) =>
        {
            if (e.GetNewValue<bool>() && box.ContextFlyout is null)
                box.ContextFlyout = Build(box);
        });
    }

    public static bool GetEnabled(TextBox box) => box.GetValue(EnabledProperty);

    public static void SetEnabled(TextBox box, bool value) => box.SetValue(EnabledProperty, value);

    private static Flyout Build(TextBox box)
    {
        var loc = LocalizationManager.Instance;
        var flyout = new Flyout { Placement = PlacementMode.Top, ShowMode = FlyoutShowMode.Transient };
        flyout.FlyoutPresenterClasses.Add("textmenu");

        var cut = Item(loc["TextMenu_Cut"], () => box.Cut());
        var copy = Item(loc["TextMenu_Copy"], () => { box.Copy(); box.ClearSelection(); });
        var paste = Item(loc["TextMenu_Paste"], () => box.Paste());
        var all = Item(loc["TextMenu_SelectAll"], () => box.SelectAll());
        var items = new[] { cut, copy, paste, all };

        var bar = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var item in items)
        {
            item.Click += (_, _) => flyout.Hide();
            bar.Children.Add(item);
        }

        flyout.Content = new Border
        {
            Classes = { "textmenu" },
            Background = Palette.Pick("#FFFFFF", "#272B33"),
            BorderBrush = Palette.Pick("#E2E8DF", "#363B44"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(4),
            Margin = new Thickness(0, 0, 0, 8),
            Child = bar
        };

        // Which actions apply is only known as the menu opens.
        flyout.Opening += (_, _) =>
        {
            cut.IsVisible = box.CanCut;
            copy.IsVisible = box.CanCopy;
            paste.IsVisible = box.CanPaste;
            all.IsVisible = !string.IsNullOrEmpty(box.Text) && box.SelectedText?.Length != box.Text.Length;
        };
        flyout.Opened += (_, _) =>
        {
            if (!items.Any(item => item.IsVisible))
                Dispatcher.UIThread.Post(flyout.Hide);
        };

        return flyout;
    }

    private static Button Item(string text, Action action)
    {
        var button = new Button { Content = text, Focusable = false };
        button.Classes.Add("textmenu");
        button.Click += (_, _) => action();
        return button;
    }
}
