using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Gheychi.App.Localization;
using Gheychi.App.Theming;
using Gheychi.App.Ui;

namespace Gheychi.App.Controls.Settings;

public enum SettingsScreenKind
{
    Spam,
    Trusted,
    Blocked,
    Sims,
    Notifications,
    Appearance,
    Privacy,
    About,
    Licenses,
    Analytics
}

/// <summary>A page of the Settings tab, built the first time it is opened and slid over the overview.</summary>
public class SettingsScreen : UserControl
{
    public event EventHandler? BackRequested;

    public event EventHandler<SettingsScreenKind>? OpenRequested;

    /// <summary>Asks to confirm title, message, accept button; the native dialog unless the Settings tab swaps in another.</summary>
    public Func<string, string, string, Task<bool>> Confirm { get; set; } = (title, message, accept) =>
        Dialogs.AlertAsync(title, message, accept, LocalizationManager.Instance["Chat_Cancel"]);

    /// <summary>Called every time the screen comes into view, to show current values.</summary>
    public virtual void OnShown()
    {
    }

    protected void RequestOpen(SettingsScreenKind kind) => OpenRequested?.Invoke(this, kind);

    internal void RequestBack() => BackRequested?.Invoke(this, EventArgs.Empty);
}

/// <summary>The 56 dip title bar of a screen: back button and title. Built in code, it is cheaper than the same XAML ten times.</summary>
public sealed class SettingsHeader : Grid
{
    private readonly TextBlock _title = new();

    public SettingsHeader()
    {
        Height = 56;
        ColumnDefinitions = new ColumnDefinitions("Auto,*");
        ColumnSpacing = 8;

        var back = new Border
        {
            Margin = new Thickness(12, 0, 0, 0),
            Child = new Icon
            {
                Data = IconCatalog.Find("back.png"),
                Width = 22,
                Height = 22,
                Foreground = Palette.Pick("#2C342E", "#E8EAED"),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            }
        };
        back.Classes.Add("backButton");
        Avalonia.Automation.AutomationProperties.SetName(back, LocalizationManager.Instance["Chat_Back"]);
        back.Tapped += (_, _) => this.FindAncestorOfType<SettingsScreen>()?.RequestBack();

        _title.Classes.Add("screenTitle");
        _title.Margin = new Thickness(0, 0, 16, 0);
        SetColumn(_title, 1);

        Children.Add(back);
        Children.Add(_title);
    }

    public string? Title
    {
        get => _title.Text;
        set => _title.Text = value;
    }
}
