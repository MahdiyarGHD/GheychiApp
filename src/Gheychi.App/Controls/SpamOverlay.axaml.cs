using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Gheychi.App.Localization;
using Gheychi.App.Ui;
using Gheychi.App.ViewModels;
using Gheychi.Core.Services;
using Gheychi.Core.Spam;

namespace Gheychi.App.Controls;

/// <summary>The spam row's ••• menu, its details card and the clear-all confirmation, shared by the Spam tab and the profile.</summary>
public partial class SpamOverlay : UserControl
{
    private readonly SpamViewModel? _viewModel;
    private readonly IDateFormattingService _dates;
    private SpamItem? _item;
    private Control? _openCard;
    private TaskCompletionSource<bool>? _confirm;
    private int _generation;
    private bool _backdropShown;

    public SpamOverlay()
    {
        InitializeComponent();
        var services = IPlatformApplication.Current?.Services;
        _viewModel = services?.GetService(typeof(SpamViewModel)) as SpamViewModel;
        _dates = services?.GetService(typeof(IDateFormattingService)) as IDateFormattingService ?? new DateFormattingService();
    }

    public void ShowMenu(SpamItem item)
    {
        _item = item;
        MenuSender.Text = item.Sender;
        MenuConfidence.Text = item.Confidence;
        MenuConfidence.Foreground = item.ConfidenceColor;
        MenuPreview.Text = item.Preview;
        _ = OpenAsync(MenuSheet, fromBottom: true);
    }

    public void ShowInfo(SpamItem item)
    {
        _item = item;
        var loc = LocalizationManager.Instance;
        var message = item.Message;

        ScorePanel.Background = item.IsVeryLikely
            ? Palette.Pick("#FBEAEA", "#3A2323")
            : Palette.Pick("#FCF2E1", "#3A3020");
        InfoPercent.Text = item.ScoreText;
        InfoPercent.Foreground = item.ConfidenceColor;
        InfoConfidence.Text = item.Confidence;
        InfoConfidence.Foreground = item.ConfidenceColor;
        InfoBar.Value = Math.Clamp(message.Score, 0f, 1f);
        InfoBar.Foreground = item.ConfidenceColor;

        var threshold = message.Threshold > 0 ? SpamItem.FormatPercent(SpamConfidence.Percent(message.Threshold)) : null;
        InfoExplain.Text = threshold is null
            ? loc["Spam_InfoExplainUnknown"]
            : string.Format(loc["Spam_InfoExplain"], threshold);

        var culture = CultureInfo.CurrentUICulture;
        InfoFrom.Text = item.Sender;
        InfoReceived.Text = $"{_dates.FormatDateSeparator(message.Timestamp, DateTime.Now, culture)} {_dates.FormatMessageTime(message.Timestamp, culture)}";
        InfoThreshold.Text = threshold ?? loc["Spam_InfoNotRecorded"];
        InfoModel.Text = string.Format(loc["Spam_InfoModelValue"], message.ModelVersion);

        _ = OpenAsync(InfoCard, fromBottom: false);
    }

    public Task<bool> ConfirmAsync(string title, string message, string accept)
    {
        _confirm?.TrySetResult(false);
        _confirm = new TaskCompletionSource<bool>();
        ConfirmTitle.Text = title;
        ConfirmMessage.Text = message;
        ConfirmAccept.Text = accept;
        _ = OpenAsync(ConfirmCard, fromBottom: false);
        return _confirm.Task;
    }

    /// <summary>True when it consumed the back press.</summary>
    public bool HandleBack()
    {
        if (!IsVisible)
            return false;

        if (_openCard is not null)
            _ = CloseAsync();
        return true;
    }

    private static double Distance(bool fromBottom) => fromBottom ? 60 : 16;

    private async Task OpenAsync(Control card, bool fromBottom)
    {
        var generation = ++_generation;
        foreach (var other in (ReadOnlySpan<Control>)[MenuSheet, InfoCard, ConfirmCard])
        {
            if (other != card)
                other.IsVisible = false;
        }

        _openCard = card;
        card.Opacity = 0;
        card.IsVisible = true;
        IsVisible = true;
        var fadeBackdrop = !_backdropShown;
        _backdropShown = true;

        // The card is invisible until the compositor has its visual; one frame, so layout is not part of the slide.
        await OverlayAnimator.SettleAsync();
        if (generation != _generation)
            return;

        await Task.WhenAll(
            fadeBackdrop ? OverlayAnimator.FadeAsync(Backdrop, 0, 1, OverlayAnimator.OpenDuration) : Task.CompletedTask,
            OverlayAnimator.FadeAsync(card, 0, 1, OverlayAnimator.OpenDuration),
            OverlayAnimator.SlideYAsync(card, Distance(fromBottom), 0, OverlayAnimator.OpenDuration, decelerate: true));
    }

    private async Task CloseAsync()
    {
        var generation = ++_generation;
        var card = _openCard;
        _openCard = null;
        _confirm?.TrySetResult(false);
        _confirm = null;
        _backdropShown = false;

        if (card is not null)
        {
            await Task.WhenAll(
                OverlayAnimator.FadeAsync(Backdrop, 1, 0, OverlayAnimator.CloseDuration),
                OverlayAnimator.FadeAsync(card, 1, 0, OverlayAnimator.CloseDuration),
                OverlayAnimator.SlideYAsync(card, 0, Distance(card == MenuSheet), OverlayAnimator.CloseDuration, decelerate: false));

            // Another card may have opened while this one was closing.
            if (generation != _generation)
                return;

            card.IsVisible = false;
        }

        IsVisible = false;
    }

    private void OnBackdropTapped(object? sender, TappedEventArgs e) => _ = CloseAsync();

    private void OnInfoTapped(object? sender, TappedEventArgs e)
    {
        if (_item is not null)
            ShowInfo(_item);
    }

    private async void OnReportTapped(object? sender, TappedEventArgs e)
    {
        var item = _item;
        await CloseAsync();
        if (item is not null)
            await SpamReport.SubmitAsync(item.Message.Body, isSpam: false);
    }

    private void OnRestoreTapped(object? sender, TappedEventArgs e) => _ = RestoreAsync(trustSender: false);

    private void OnRestoreTrustTapped(object? sender, TappedEventArgs e) => _ = RestoreAsync(trustSender: true);

    private async Task RestoreAsync(bool trustSender)
    {
        var item = _item;
        await CloseAsync();
        if (item is null || _viewModel is null)
            return;

        try
        {
            if (!await _viewModel.RestoreAsync(item, trustSender))
            {
                var loc = LocalizationManager.Instance;
                await Dialogs.AlertAsync(string.Empty, loc["Spam_RestoreFailed"], loc["Spam_Ok"]);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Restoring spam failed: {ex}");
        }
    }

    private async void OnDeleteTapped(object? sender, TappedEventArgs e)
    {
        var item = _item;
        await CloseAsync();
        if (item is null || _viewModel is null)
            return;

        try
        {
            await _viewModel.DeleteAsync(item);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Deleting spam failed: {ex}");
        }
    }

    private void OnConfirmCancelTapped(object? sender, TappedEventArgs e) => _ = CloseAsync();

    private void OnConfirmAcceptTapped(object? sender, TappedEventArgs e)
    {
        _confirm?.TrySetResult(true);
        _confirm = null;
        _ = CloseAsync();
    }
}
