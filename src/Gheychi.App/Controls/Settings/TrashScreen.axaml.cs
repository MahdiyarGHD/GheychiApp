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
    private bool _busy;

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
            // Off the UI thread: one contacts lookup per sender.
            var rows = await Task.Run(() => groups.Select(group => ToRow(group, now)).ToList());
            if (version != _loadVersion)
                return;

            TrashList.ItemsSource = rows;
            EmptyNote.IsVisible = rows.Count == 0;
            EmptyAll.IsVisible = rows.Count > 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Listing the trash failed: {ex}");
        }
    }

    private static TrashRow ToRow(TrashGroup group, DateTime now)
    {
        var loc = LocalizationManager.Instance;
        var title = ConversationReader.ReadContactName(Platform.AppContext, group.Address)
                    ?? (PhoneNumberNormalizer.IsAlphanumeric(group.Address) ? group.Address : PhoneNumberNormalizer.FormatDisplay(group.Address));
        var days = string.Format(loc["Trash_DaysLeft"], SettingsUi.Number(TrashPolicy.DaysLeft(group.TrashedAtMs, now)));
        var meta = group.Messages.Count > 1
            ? string.Format(loc["Trash_MessageCount"], SettingsUi.Number(group.Messages.Count)) + " · " + days
            : days;
        return new TrashRow(group, title, MessagePreview.SingleLine(group.Newest.Body), meta);
    }

    private async void OnRestoreTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not TrashRow row || _trash is null || _sms is null || _busy)
            return;

        _busy = true;
        try
        {
            var loc = LocalizationManager.Instance;
            var restored = await _sms.RestoreMessagesAsync(row.Group.Messages);
            if (restored == 0)
            {
                Toast.Show(loc["Trash_RestoreFailed"]);
                return;
            }

            // Only what came back leaves the trash: a partial restore keeps the rest to try again.
            if (restored == row.Group.Messages.Count)
                await _trash.DeleteAsync(row.Group.Ids);
            Toast.Show(loc["Trash_Restored"]);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Restoring from the trash failed: {ex}");
        }
        finally
        {
            _busy = false;
        }
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
