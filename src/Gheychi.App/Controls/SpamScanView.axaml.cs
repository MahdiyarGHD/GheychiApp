using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Gheychi.App.Localization;
using Gheychi.App.Ui;
using Gheychi.App.ViewModels;
using Gheychi.Core.Services;
using Gheychi.Core.Spam;

namespace Gheychi.App.Controls;

/// <summary>
/// The history scan over the Spam tab: start, progress, the review of what it found, and later the batch a scan moved
/// to Spam. Closing it does not stop a running scan.
/// </summary>
public partial class SpamScanView : UserControl
{
    private readonly SpamScanViewModel? _scan;
    private readonly SpamViewModel? _spam;
    private readonly HashSet<string> _batchExpanded = new(StringComparer.Ordinal);
    private IReadOnlyList<ScanGroupRow> _batchGroups = [];
    // 0 shows the scan itself; otherwise the batch that scan moved.
    private long _batchId;

    public SpamScanView()
    {
        InitializeComponent();
        var services = IPlatformApplication.Current?.Services;
        _scan = services?.GetService<SpamScanViewModel>();
        _spam = services?.GetService<SpamViewModel>();
        if (_scan is not null)
            _scan.Changed += Update;
        if (_spam is not null)
            _spam.Changed += () =>
            {
                if (_batchId != 0)
                    Update();
            };
    }

    public void OpenScan()
    {
        _batchId = 0;
        IsVisible = true;
        if (_scan is { Stage: ScanStage.Idle })
            _ = _scan.PrepareAsync();
        Update();
    }

    public void OpenBatch(long scanId)
    {
        _batchId = scanId;
        _batchExpanded.Clear();
        _batchGroups = [];
        IsVisible = true;
        Update();
    }

    /// <summary>True when it consumed the back press.</summary>
    public bool HandleBack()
    {
        if (!IsVisible)
            return false;

        Close();
        return true;
    }

    private void Close()
    {
        IsVisible = false;
        _batchId = 0;
        _scan?.Dismiss();
    }

    private void Update()
    {
        if (!IsVisible || _scan is null)
            return;

        if (_batchId != 0)
        {
            ShowBatch();
            return;
        }

        var loc = LocalizationManager.Instance;
        TitleText.Text = loc["Scan_Title"];
        HeaderAction.IsVisible = false;
        var stage = _scan.Stage;
        StartPanel.IsVisible = stage == ScanStage.Idle;
        ScanningPanel.IsVisible = stage == ScanStage.Scanning;
        ApplyingPanel.IsVisible = stage == ScanStage.Applying;
        FinishedPanel.IsVisible = stage == ScanStage.Finished;
        ListPanel.IsVisible = BottomBar.IsVisible = stage == ScanStage.Review;

        switch (stage)
        {
            case ScanStage.Idle:
                StartTotal.Text = _scan.ReceivedCount is { } received
                    ? string.Format(loc["Scan_Total"], LocalizedNumbers.Number(received))
                    : string.Empty;
                StartSkipped.Text = string.Format(loc["Scan_Skipped"], LocalizedNumbers.Number(_scan.KeptCount));
                StartWarning.Text = _scan.FilterOff ? loc["Scan_FilterOff"] : string.Empty;
                StartWarning.IsVisible = _scan.FilterOff;
                StartButton.Opacity = _scan.FilterOff ? 0.4 : 1;
                break;

            case ScanStage.Scanning:
                ScanPercent.Text = SpamItem.FormatPercent(_scan.Percent);
                ScanBar.Value = _scan.Fraction;
                ScanProgress.Text = _scan.Total == 0
                    ? loc["Scan_Reading"]
                    : string.Format(loc["Scan_Progress"], LocalizedNumbers.Number(_scan.Checked), LocalizedNumbers.Number(_scan.Total));
                ScanFound.Text = string.Format(loc["Scan_Found"], LocalizedNumbers.Number(_scan.Found));
                break;

            case ScanStage.Finished:
                FinishedText.Text = _scan.FinishedText;
                FinishedDetail.Text = _scan.FinishedDetail;
                FinishedDetail.IsVisible = _scan.FinishedDetail.Length > 0;
                break;

            case ScanStage.Review:
                ListHeader.Text = _scan.ReviewHeader;
                ListNote.Text = _scan.ReviewNote;
                ListNote.IsVisible = _scan.ReviewNote.Length > 0;
                Chips.IsVisible = _scan.HasMaybe;
                ChipAll.Classes.Set("on", _scan.Filter == ScanFilter.All);
                ChipStrong.Classes.Set("on", _scan.Filter == ScanFilter.Strong);
                ChipMaybe.Classes.Set("on", _scan.Filter == ScanFilter.Maybe);
                if (!ReferenceEquals(RowList.ItemsSource, _scan.Rows))
                    RowList.ItemsSource = _scan.Rows;
                var selected = _scan.SelectedCount;
                MoveText.Text = string.Format(loc["Scan_Move"], LocalizedNumbers.Number(selected));
                MoveButton.Opacity = selected > 0 ? 1 : 0.4;
                break;
        }
    }

    private void ShowBatch()
    {
        if (_spam?.Batch(_batchId) is not { } batch || _scan is null)
        {
            // Every message of it was restored or deleted.
            Close();
            return;
        }

        var loc = LocalizationManager.Instance;
        TitleText.Text = loc["Scan_BatchTitle"];
        HeaderAction.Text = loc["Scan_RestoreAll"];
        HeaderAction.IsVisible = true;
        StartPanel.IsVisible = ScanningPanel.IsVisible = ApplyingPanel.IsVisible = FinishedPanel.IsVisible = BottomBar.IsVisible = false;
        ListPanel.IsVisible = true;
        Chips.IsVisible = false;
        ListHeader.Text = batch.Title;
        ListNote.Text = batch.Subtitle;
        ListNote.IsVisible = true;

        var first = _batchGroups.Count == 0;
        _batchGroups = _scan.BatchGroups(batch, _batchExpanded);
        if (first)
        {
            // Small senders open by default; big ones stay folded so the list starts short.
            foreach (var group in _batchGroups.Where(g => g.Hits.Count <= 3))
            {
                group.Expanded = true;
                _batchExpanded.Add(group.Key);
            }
        }

        ShowBatchRows();
    }

    private void ShowBatchRows()
    {
        var rows = new List<object>();
        foreach (var group in _batchGroups)
        {
            rows.Add(group);
            if (group.Expanded)
                rows.AddRange(group.Hits);
        }

        RowList.ItemsSource = rows;
    }

    private void OnBackTapped(object? sender, TappedEventArgs e) => Close();

    private void OnStartTapped(object? sender, TappedEventArgs e)
    {
        if (_scan is not null)
            _ = _scan.StartAsync();
    }

    private void OnStopTapped(object? sender, TappedEventArgs e) => _scan?.Stop();

    private void OnDoneTapped(object? sender, TappedEventArgs e) => Close();

    private void OnChipTapped(object? sender, TappedEventArgs e)
    {
        var filter = sender == ChipStrong ? ScanFilter.Strong : sender == ChipMaybe ? ScanFilter.Maybe : ScanFilter.All;
        _scan?.SetFilter(filter);
    }

    private async void OnRowsTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (_scan is null || (e.Source as StyledElement)?.DataContext is not { } data)
                return;

            var tag = TagOf(e.Source as Visual);
            switch (data)
            {
                case ScanGroupRow group when tag == "check" && _batchId == 0:
                    _scan.ToggleGroup(group);
                    break;

                case ScanGroupRow group when _batchId == 0:
                    _scan.ToggleExpanded(group);
                    break;

                case ScanGroupRow group:
                    group.Expanded = !group.Expanded;
                    if (group.Expanded)
                        _batchExpanded.Add(group.Key);
                    else
                        _batchExpanded.Remove(group.Key);
                    ShowBatchRows();
                    break;

                case ScanMessageRow row when tag == "more" || _batchId != 0:
                    await ShowActionsAsync(row);
                    break;

                case ScanMessageRow row:
                    _scan.Toggle(row);
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Scan row tap failed: {ex}");
        }
    }

    // The nearest string Tag between the tapped element and its row ("check", "more"); row templates carry object tags.
    private string? TagOf(Visual? visual)
    {
        for (var v = visual; v is not null && v != RowList; v = v.GetVisualParent())
        {
            if (v is Control { Tag: string tag })
                return tag;
        }

        return null;
    }

    private async Task ShowActionsAsync(ScanMessageRow row)
    {
        if (_scan is null)
            return;

        var loc = LocalizationManager.Instance;
        var notSpam = loc["Scan_NotSpam"];
        var reportSpam = loc["Scan_ReportSpam"];

        if (row.Item is { } item)
        {
            var delete = loc["Spam_ActionDelete"];
            var choice = await Dialogs.ActionSheetAsync(row.Sender, loc["Chat_Cancel"], delete, notSpam, reportSpam);
            if (choice == notSpam)
            {
                if (_spam is null || !await _spam.RestoreAsync(item, trustSender: false))
                {
                    await Dialogs.AlertAsync(string.Empty, loc["Spam_RestoreFailed"], loc["Spam_Ok"]);
                    return;
                }

                if (await AskReportAsync())
                    await SpamReport.SubmitAsync(row.Body, isSpam: false);
            }
            else if (choice == reportSpam)
            {
                await SpamReport.SubmitAsync(row.Body, isSpam: true);
            }
            else if (choice == delete && _spam is not null)
            {
                await _spam.DeleteAsync(item);
            }

            return;
        }

        if (row.Kept)
        {
            // Already judged not spam; all that is left is reporting it.
            if (await Dialogs.ActionSheetAsync(row.Sender, loc["Chat_Cancel"], null, loc["Scan_ReportNotSpam"]) == loc["Scan_ReportNotSpam"])
                await SpamReport.SubmitAsync(row.Body, isSpam: false);
            return;
        }

        var trust = loc["Scan_TrustSender"];
        var picked = await Dialogs.ActionSheetAsync(row.Sender, loc["Chat_Cancel"], null, notSpam, reportSpam, trust);
        if (picked == notSpam)
        {
            await _scan.MarkNotSpamAsync(row);
            if (await AskReportAsync())
                await SpamReport.SubmitAsync(row.Body, isSpam: false);
        }
        else if (picked == reportSpam)
        {
            await SpamReport.SubmitAsync(row.Body, isSpam: true);
        }
        else if (picked == trust && _scan.GroupOf(row) is { } group)
        {
            _scan.TrustSender(group);
        }
    }

    private static Task<bool> AskReportAsync()
    {
        var loc = LocalizationManager.Instance;
        return Dialogs.AlertAsync(loc["Scan_NotSpam"], loc["Scan_NotSpamAsk"], loc["Scan_Report"], loc["Scan_NoReport"]);
    }

    private async void OnHeaderActionTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (_spam?.Batch(_batchId) is not { } batch)
                return;

            var loc = LocalizationManager.Instance;
            var count = LocalizedNumbers.Number(batch.Items.Count);
            if (!await Dialogs.AlertAsync(string.Format(loc["Scan_RestoreAllTitle"], count), loc["Scan_RestoreAllBody"], loc["Scan_RestoreAll"], loc["Chat_Cancel"]))
                return;

            var total = batch.Items.Count;
            var restored = await _spam.RestoreBatchAsync(batch.ScanId, keep: true);
            if (restored < total)
                await Dialogs.AlertAsync(string.Empty, loc["Spam_RestoreFailed"], loc["Spam_Ok"]);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Restoring a scan batch failed: {ex}");
        }
    }

    private async void OnDiscardTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            var loc = LocalizationManager.Instance;
            if (_scan is not null && await Dialogs.AlertAsync(loc["Scan_DiscardTitle"], loc["Scan_DiscardBody"], loc["Scan_Discard"], loc["Chat_Cancel"], danger: true))
                await _scan.DiscardAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Discarding the scan failed: {ex}");
        }
    }

    private async void OnMoveTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (_scan is not { Stage: ScanStage.Review } || _spam is null)
                return;

            var count = _scan.SelectedCount;
            if (count == 0)
                return;

            var threads = await _scan.CountEmptiedThreadsAsync();
            var choice = await ConfirmMoveAsync(count, threads, _spam.ExpiresOn(DateTime.Now));
            if (choice == MoveChoice.Cancel)
                return;

            var bodies = _scan.SelectedMessages.Select(m => m.Body).ToList();
            var loc = LocalizationManager.Instance;
            var (result, moved, changed) = await _scan.ApplyAsync();
            switch (result)
            {
                case ApplyResult.NotDefaultApp:
                    await Dialogs.AlertAsync(string.Empty, loc["Scan_NeedDefaultApp"], loc["Spam_Ok"]);
                    return;
                case ApplyResult.NothingMoved:
                    await Dialogs.AlertAsync(string.Empty, loc["Scan_NothingMoved"], loc["Spam_Ok"]);
                    return;
            }

            Close();
            if (changed > 0)
                Toast.Show(string.Format(loc["Scan_Changed"], LocalizedNumbers.Number(changed)));
            if (choice == MoveChoice.MoveAndReport && moved > 0)
                _ = ReportAllAsync(bodies);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Moving scanned spam failed: {ex}");
        }
    }

    private enum MoveChoice
    {
        Cancel,
        Move,
        MoveAndReport
    }

    private static Task<MoveChoice> ConfirmMoveAsync(int count, int threads, DateTime expires)
    {
        var loc = LocalizationManager.Instance;
        var dates = IPlatformApplication.Current?.Services.GetService<IDateFormattingService>() ?? new DateFormattingService();
        var day = dates.FormatDateSeparator(expires, DateTime.Now, System.Globalization.CultureInfo.CurrentUICulture);

        return Sheet.ShowAsync<MoveChoice>(finish =>
        {
            var report = false;
            var glyph = new TextBlock { FontSize = 12, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var check = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(1.5), Child = glyph };
            void Paint()
            {
                check.Background = report ? Sheet.Accent : Palette.Transparent;
                check.BorderBrush = report ? Sheet.Accent : Sheet.Muted;
                glyph.Text = report ? "✓" : string.Empty;
            }
            Paint();

            var reportRow = Sheet.Tappable(
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    Children = { check, Sheet.Label(loc["Scan_AlsoReport"], 14.5, Sheet.Ink) }
                },
                () =>
                {
                    report = !report;
                    Paint();
                },
                new Thickness(0, 4),
                minHeight: 44);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(Sheet.Button(loc["Chat_Cancel"], filled: false, () => finish(MoveChoice.Cancel), Sheet.Muted));
            buttons.Children.Add(Sheet.Button(loc["Scan_MoveConfirm"], filled: true, () => finish(report ? MoveChoice.MoveAndReport : MoveChoice.Move)));

            var body = new StackPanel { Margin = new Thickness(24, 22, 18, 14), Spacing = 10 };
            body.Children.Add(Sheet.Label(string.Format(loc["Scan_MoveTitle"], LocalizedNumbers.Number(count)), 18, Sheet.Ink, bold: true));
            body.Children.Add(Sheet.Label(string.Format(loc["Scan_MoveBody"], day), 14.5, Sheet.Soft));
            if (threads > 0)
                body.Children.Add(Sheet.Label(string.Format(loc["Scan_MoveThreads"], LocalizedNumbers.Number(threads)), 14.5, Sheet.Soft));
            body.Children.Add(reportRow);
            body.Children.Add(buttons);
            return body;
        }, MoveChoice.Cancel, centered: true);
    }

    // The form takes one message per post; numbers and links are masked by the reporter before anything is sent.
    private static async Task ReportAllAsync(IReadOnlyList<string> bodies)
    {
        if (IPlatformApplication.Current?.Services.GetService<ISpamReporter>() is not { } reporter)
            return;

        var failed = await Task.Run(async () =>
        {
            var failures = 0;
            foreach (var body in bodies)
            {
                try
                {
                    await reporter.ReportAsync(body, isSpam: true);
                }
                catch (Exception ex)
                {
                    failures++;
                    System.Diagnostics.Debug.WriteLine($"Reporting scanned spam failed: {ex}");
                }
            }
            return failures;
        });

        var loc = LocalizationManager.Instance;
        MainThread.BeginInvokeOnMainThread(() => Toast.Show(loc[failed == 0 ? "Spam_ReportThanks" : "Spam_ReportFailed"]));
    }
}
