using Avalonia.Controls;
using Avalonia.Input;
using Gheychi.App.Platforms.Android.Notifications;
using Gheychi.App.ViewModels;
using Gheychi.Core.Services;

namespace Gheychi.App.Controls.Settings;

public partial class BlockedSendersScreen : SettingsScreen
{
    private readonly IBlockedSenders? _blocked;
    private int _loadVersion;

    public BlockedSendersScreen()
    {
        InitializeComponent();
        _blocked = IPlatformApplication.Current?.Services.GetService<IBlockedSenders>();
    }

    public override void OnShown() => _ = LoadAsync();

    private async Task LoadAsync()
    {
        if (_blocked is null)
            return;

        var version = ++_loadVersion;
        var keys = _blocked.GetAll();
        try
        {
            // Off the UI thread: one contacts lookup per sender.
            var rows = await Task.Run(() => keys
                .Select(ToRow)
                .OrderBy(row => row.Title, StringComparer.CurrentCulture)
                .ToList());
            if (version != _loadVersion)
                return;

            BlockedList.ItemsSource = rows;
            EmptyNote.IsVisible = rows.Count == 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Listing blocked senders failed: {ex}");
        }
    }

    private static BlockedSenderRow ToRow(string key)
    {
        var display = PhoneNumberNormalizer.FormatDisplay(key);
        var name = ConversationReader.ReadContactName(Platform.AppContext, key);
        return name is null ? new BlockedSenderRow(key, display, string.Empty) : new BlockedSenderRow(key, name, display);
    }

    private void OnUnblockTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not BlockedSenderRow row || _blocked is null)
            return;

        _blocked.SetBlocked(row.Key, false);
        _ = LoadAsync();
    }
}
