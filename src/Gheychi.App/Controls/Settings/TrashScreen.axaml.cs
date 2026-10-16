using Avalonia.Controls;
using Avalonia.Input;
using Gheychi.App.Localization;
using Gheychi.App.Platforms.Android.Notifications;
using Gheychi.App.Ui;
using Gheychi.App.ViewModels;
using Gheychi.Core.Services;

namespace Gheychi.App.Controls.Settings;

/// <summary>Deleted messages, kept for <see cref="TrashPolicy.RetentionDays"/> days: put back, or erased for good.</summary>
public partial class TrashScreen : SettingsScreen
{
    private readonly ITrashRepository? _trash;
    private readonly ISmsService? _sms;
    private int _loadVersion;
    private List<TrashRow>? _rows;

    // A contact lookup is a query to the system: kept for a while, the list and each restore would repeat them.
    private static readonly TimeSpan NameCacheLife = TimeSpan.FromMinutes(5);
    private readonly Dictionary<string, string> _names = [];
    private DateTime _namesFrom = DateTime.MinValue;

    public TrashScreen()
    {
        InitializeComponent();
        var services = IPlatformApplication.Current?.Services;
        _trash = services?.GetService<ITrashRepository>();
        _sms = services?.GetService<ISmsService>();
        Note.Text = string.Format(LocalizationManager.Instance["Trash_Note"], SettingsUi.Number(TrashPolicy.RetentionDays));
    }

    public override void OnShown() => _ = LoadAsync();

    private async Task LoadAsync()
    {
        if (_trash is null)
            return;

        var version = ++_loadVersion;
        try
        {
            var now = DateTime.Now;
            await _trash.DeleteOlderThanAsync(TrashPolicy.Cutoff(now));
            var groups = TrashPolicy.Group(await _trash.GetAllAsync());
            if (now - _namesFrom > NameCacheLife)
            {
                lock (_names)
                    _names.Clear();
                _namesFrom = now;
            }

            // Off the UI thread: one contacts lookup per sender, once.
            var rows = await Task.Run(() => groups.Select(group => ToRow(group, now)).ToList());
            if (version != _loadVersion)
                return;

            Show(rows);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Listing the trash failed: {ex}");
        }
    }

    private string NameOf(string address)
    {
        lock (_names)
        {
            if (_names.TryGetValue(address, out var known))
                return known;
        }

        var name = ConversationReader.ReadContactName(Platform.AppContext, address)
                   ?? (PhoneNumberNormalizer.IsAlphanumeric(address) ? address : PhoneNumberNormalizer.FormatDisplay(address));
        lock (_names)
            _names[address] = name;
        return name;
    }

    private void Show(List<TrashRow> rows)
    {
        _rows = rows;
        TrashList.ItemsSource = rows;
        EmptyNote.IsVisible = rows.Count == 0;
        EmptyAll.IsVisible = rows.Count > 0;
    }

    private TrashRow ToRow(TrashGroup group, DateTime now)
    {
        var loc = LocalizationManager.Instance;
        var title = NameOf(group.Address);
        var days = string.Format(loc["Trash_DaysLeft"], SettingsUi.Number(TrashPolicy.DaysLeft(group.TrashedAtMs, now)));
        var meta = group.Messages.Count > 1
            ? string.Format(loc["Trash_MessageCount"], SettingsUi.Number(group.Messages.Count)) + " · " + days
            : days;
        return new TrashRow(group, title, MessagePreview.SingleLine(group.Newest.Body), meta);
    }

    private async void OnRestoreTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not TrashRow row || _trash is null || _sms is null || _rows is null)
            return;

        // The row leaves the list and the toast shows at once; the store is written behind it. Waiting for the write made
        // the tap look dead for a couple of seconds.
        var loc = LocalizationManager.Instance;
        Show(_rows.Where(r => !ReferenceEquals(r, row)).ToList());
        Toast.Show(loc["Trash_Restored"]);

        try
        {
            var restored = await _sms.RestoreMessagesAsync(row.Group.Messages);
            // Only what came back leaves the trash: a partial restore keeps the rest to try again.
            if (restored == row.Group.Messages.Count)
            {
                // The list already shows it gone: nothing to read again.
                await _trash.DeleteAsync(row.Group.Ids);
                return;
            }

            Toast.Show(loc["Trash_RestoreFailed"]);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Restoring from the trash failed: {ex}");
            Toast.Show(loc["Trash_RestoreFailed"]);
        }

        // Something did not come back: show what is still in the trash.
        await LoadAsync();
    }

    private async void OnDeleteForeverTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not TrashRow row || _trash is null)
            return;

        var loc = LocalizationManager.Instance;
        if (!await Confirm(loc["Trash_DeleteForeverTitle"], loc["Trash_DeleteForeverMessage"], loc["Trash_DeleteForever"]))
            return;

        await _trash.DeleteAsync(row.Group.Ids);
        await LoadAsync();
    }

    private async void OnEmptyAllTapped(object? sender, TappedEventArgs e)
    {
        if (_trash is null)
            return;

        var loc = LocalizationManager.Instance;
        if (!await Confirm(loc["Trash_EmptyAllTitle"], loc["Trash_EmptyAllMessage"], loc["Trash_EmptyAllButton"]))
            return;

        await _trash.DeleteAllAsync();
        await LoadAsync();
    }
}
