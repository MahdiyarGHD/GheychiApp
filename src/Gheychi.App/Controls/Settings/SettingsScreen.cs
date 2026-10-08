
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
public class SettingsScreen : ContentView
{
    public event EventHandler? BackRequested;

    public event EventHandler<SettingsScreenKind>? OpenRequested;

    /// <summary>Shows the shared confirmation card; set by the Settings tab.</summary>
    public Func<string, string, string, Task<bool>>? Confirm { get; set; }

    /// <summary>Called every time the screen comes into view, to show current values.</summary>
    public virtual void OnShown()
    {
    }

    protected void RequestOpen(SettingsScreenKind kind) => OpenRequested?.Invoke(this, kind);

    protected void OnBackTapped(object? sender, TappedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Call after InitializeComponent: the chevrons point along the reading direction.</summary>
    protected void MirrorChevrons() => SettingsUi.MirrorChevrons(this, Resources);
}
