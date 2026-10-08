using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Gheychi.App.Localization;
using Gheychi.App.ViewModels;
using Gheychi.Core.Services;
using Gheychi.Core.Spam;

namespace Gheychi.App.Controls.Settings;

public partial class SpamAnalyticsScreen : SettingsScreen
{
    private readonly SpamViewModel? _spam;
    private int _loadVersion;

    public SpamAnalyticsScreen()
    {
        InitializeComponent();
        _spam = IPlatformApplication.Current?.Services.GetService<SpamViewModel>();
        TotalCaption.Text = LocalizationManager.Instance["Settings_StatTotal"];
    }

    public override void OnShown() => _ = LoadAsync(++_loadVersion);

    private async Task LoadAsync(int version)
    {
        try
        {
            var analytics = _spam is null ? SpamAnalytics.Empty : await _spam.GetAnalyticsAsync();
            if (version == _loadVersion)
                Show(analytics);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Reading spam analytics failed: {ex}");
        }
    }

    private void Show(SpamAnalytics analytics)
    {
        var loc = LocalizationManager.Instance;
        TotalValue.Text = SettingsUi.Number(analytics.Total);
        TodayValue.Text = SettingsUi.Number(analytics.Today);
        WeekValue.Text = SettingsUi.Number(analytics.Last7Days);
        MonthValue.Text = SettingsUi.Number(analytics.Last30Days);

        EmptyNote.IsVisible = analytics.Total == 0;
        Details.IsVisible = analytics.Total > 0;
        if (analytics.Total == 0)
            return;

        BuildChart(analytics.Daily);

        ConfidenceBar.ColumnDefinitions = new ColumnDefinitions
        {
            new ColumnDefinition(new GridLength(analytics.VeryLikely, GridUnitType.Star)),
            new ColumnDefinition(new GridLength(analytics.Likely, GridUnitType.Star))
        };
        VeryLikelyLegend.Text = $"{loc["Spam_VeryLikely"]} · {SettingsUi.Number(analytics.VeryLikely)}";
        LikelyLegend.Text = $"{loc["Spam_Likely"]} · {SettingsUi.Number(analytics.Likely)}";

        AccuracyValue.Text = string.Format(loc["Settings_AccuracyValue"], SpamItem.FormatPercent(analytics.AccuracyPercent ?? 100));
        AccuracyHint.Text = analytics.Restored == 0
            ? loc["Settings_AccuracyNone"]
            : string.Format(loc["Settings_AccuracyHint"], SettingsUi.Number(analytics.Restored));

        if (analytics.BusiestHour is { } hour)
        {
            var range = $"{hour:00}:00–{(hour + 1) % 24:00}:00";
            BusiestValue.Text = string.Format(loc["Settings_BusiestValue"], SettingsUi.Digits(range));
        }

        var senders = analytics.TopSenders
            .Select(s => new TopSenderRow(PhoneNumberNormalizer.FormatDisplay(s.SenderKey), SettingsUi.Number(s.Count)))
            .ToList();
        TopSendersSection.IsVisible = senders.Count > 0;
        TopSenderList.ItemsSource = senders;
    }

    // Fourteen bars drawn by one control, today on the reading-direction end; every other day is labelled so today always is.
    private void BuildChart(IReadOnlyList<int> daily)
    {
        DailyChart.Values = daily;

        DailyLabels.Children.Clear();
        var columns = new ColumnDefinitions();
        for (var i = 0; i < daily.Count; i++)
            columns.Add(new ColumnDefinition(GridLength.Star));
        DailyLabels.ColumnDefinitions = columns;

        var today = DateTime.Today;
        var persian = new PersianCalendar();
        for (var i = 0; i < daily.Count; i++)
        {
            if ((daily.Count - 1 - i) % 2 != 0)
                continue;

            var date = today.AddDays(i - (daily.Count - 1));
            var day = SettingsUi.IsPersian ? persian.GetDayOfMonth(date) : date.Day;
            var label = new TextBlock
            {
                Text = SettingsUi.Number(day),
                FontSize = 10,
                Margin = new Thickness(0, 4, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            label.Classes.Add("note");
            Grid.SetColumn(label, i);
            DailyLabels.Children.Add(label);
        }
    }
}
