using System.Globalization;
using Gheychi.App.Localization;
using Gheychi.App.ViewModels;
using Gheychi.Core.Services;
using Gheychi.Core.Spam;

namespace Gheychi.App.Controls;

/// <summary>The spam row's ••• menu, its details card and the clear-all confirmation, shared by the Spam tab and the profile.</summary>
public partial class SpamOverlay : ContentView
{
    private const uint AnimationMs = 160;

    private static readonly Color VeryLikelyPanelLight = Color.FromArgb("#FBEAEA");
    private static readonly Color VeryLikelyPanelDark = Color.FromArgb("#3A2323");
    private static readonly Color LikelyPanelLight = Color.FromArgb("#FCF2E1");
    private static readonly Color LikelyPanelDark = Color.FromArgb("#3A3020");

    private readonly SpamViewModel? _viewModel;
    private readonly IDateFormattingService _dates;
    private SpamItem? _item;
    private View? _openCard;
    private TaskCompletionSource<bool>? _confirm;

    public SpamOverlay()
    {
        InitializeComponent();
        var services = IPlatformApplication.Current?.Services;
        _viewModel = services?.GetService<SpamViewModel>();
        _dates = services?.GetService<IDateFormattingService>() ?? new DateFormattingService();
    }

    private static bool IsDark => Application.Current?.RequestedTheme == AppTheme.Dark;

    public void ShowMenu(SpamItem item)
    {
        _item = item;
        MenuSender.Text = item.Sender;
        MenuConfidence.Text = item.Confidence;
        MenuConfidence.TextColor = item.ConfidenceColor;
        MenuPreview.Text = item.Preview;
        _ = OpenAsync(MenuSheet, fromBottom: true);
    }

    public void ShowInfo(SpamItem item)
    {
        _item = item;
        var loc = LocalizationManager.Instance;
        var message = item.Message;

        ScorePanel.BackgroundColor = item.IsVeryLikely
            ? IsDark ? VeryLikelyPanelDark : VeryLikelyPanelLight
            : IsDark ? LikelyPanelDark : LikelyPanelLight;
        InfoPercent.Text = item.ScoreText;
        InfoPercent.TextColor = item.ConfidenceColor;
        InfoConfidence.Text = item.Confidence;
        InfoConfidence.TextColor = item.ConfidenceColor;
        InfoBar.Progress = Math.Clamp(message.Score, 0f, 1f);
        InfoBar.ProgressColor = item.ConfidenceColor;

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

        _ = CloseAsync();
        return true;
    }

    private async Task OpenAsync(View card, bool fromBottom)
    {
        if (_openCard is not null && _openCard != card)
            _openCard.IsVisible = false;

        _openCard = card;
        IsVisible = true;
        card.IsVisible = true;
        card.Opacity = 0;
        card.TranslationY = fromBottom ? 60 : 16;

        await Task.WhenAll(
            Backdrop.FadeToAsync(1, AnimationMs),
            card.FadeToAsync(1, AnimationMs),
            card.TranslateToAsync(0, 0, AnimationMs, Easing.CubicOut));
    }

    private async Task CloseAsync()
    {
        var card = _openCard;
        _openCard = null;
        _confirm?.TrySetResult(false);
        _confirm = null;

        if (card is not null)
        {
            await Task.WhenAll(
                Backdrop.FadeToAsync(0, AnimationMs),
                card.FadeToAsync(0, AnimationMs),
                card.TranslateToAsync(0, card == MenuSheet ? 60 : 16, AnimationMs, Easing.CubicIn));
            card.IsVisible = false;
        }

        // Another card may have opened while this one was closing.
        if (_openCard is null)
            IsVisible = false;
    }

    private void OnBackdropTapped(object? sender, TappedEventArgs e) => _ = CloseAsync();

    private void OnInfoTapped(object? sender, TappedEventArgs e)
    {
        if (_item is not null)
            ShowInfo(_item);
    }

    // TODO: keep the report (text + verdict) so it can feed the next model; for now it only thanks the user.
    private async void OnReportTapped(object? sender, TappedEventArgs e)
    {
        await CloseAsync();
#if ANDROID
        global::Android.Widget.Toast.MakeText(Platform.AppContext, LocalizationManager.Instance["Spam_ReportThanks"], global::Android.Widget.ToastLength.Short)?.Show();
#endif
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
            if (!await _viewModel.RestoreAsync(item, trustSender) && Shell.Current is { } shell)
            {
                var loc = LocalizationManager.Instance;
                await shell.DisplayAlertAsync(string.Empty, loc["Spam_RestoreFailed"], loc["Spam_Ok"]);
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
