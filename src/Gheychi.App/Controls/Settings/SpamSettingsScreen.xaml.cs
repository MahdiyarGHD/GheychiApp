using Gheychi.App.Localization;
using Gheychi.App.ViewModels;
using Gheychi.Core.Spam;

namespace Gheychi.App.Controls.Settings;

public partial class SpamSettingsScreen : SettingsScreen
{
    private const int MinThresholdPercent = 50;
    private const int MaxThresholdPercent = 99;
    private static readonly int[] RetentionChoices = [7, 14, 30, 90];

    private readonly ISpamSettings? _settings;
    private readonly ISpamModelUpdater? _models;
    private readonly SpamViewModel? _spam;
    private readonly (Border Segment, int Percent)[] _presets;
    private bool _binding;

    public SpamSettingsScreen()
    {
        InitializeComponent();
        MirrorChevrons();

        var services = IPlatformApplication.Current?.Services;
        _settings = services?.GetService<ISpamSettings>();
        _models = services?.GetService<ISpamModelUpdater>();
        _spam = services?.GetService<SpamViewModel>();

        _presets = [(PresetRelaxed, 95), (PresetBalanced, 85), (PresetStrict, 70)];
        foreach (var (segment, percent) in _presets)
            ((Label)((Layout)segment.Content!).Children[1]).Text = SpamItem.FormatPercent(percent);
    }

    private int ThresholdPercent =>
        Math.Clamp((int)MathF.Round((_settings?.Threshold ?? SpamDetector.DefaultThreshold) * 100f), MinThresholdPercent, MaxThresholdPercent);

    private int RetentionDays => _spam?.RetentionDays ?? _settings?.RetentionDays ?? SpamDetector.DefaultRetentionDays;

    public override void OnShown()
    {
        var loc = LocalizationManager.Instance;
        _binding = true;
        FilterSwitch.IsToggled = _settings?.Enabled ?? true;
        SensitivitySlider.Value = ThresholdPercent;
        _binding = false;

        ShowFilterState();
        ShowSensitivity(ThresholdPercent);
        AutoClearValue.Text = SettingsUi.Days(RetentionDays);

        var trusted = _spam?.TrustedSenders().Count ?? 0;
        TrustedHint.Text = trusted == 0 ? loc["Settings_TrustedNone"] : string.Format(loc["Settings_TrustedHint"], SettingsUi.Number(trusted));

        ModelVersion.Text = loc["Settings_ModelNone"];
        _ = ShowModelVersionAsync();
    }

    private async Task ShowModelVersionAsync()
    {
        try
        {
            if (_models is not null && await _models.GetActiveVersionAsync() is { } version)
                ModelVersion.Text = string.Format(LocalizationManager.Instance["Spam_InfoModelValue"], SettingsUi.Number(version));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Reading the spam model version failed: {ex}");
        }
    }

    private void ShowFilterState()
    {
        var enabled = FilterSwitch.IsToggled;
        FilterHint.Text = LocalizationManager.Instance[enabled ? "Settings_SpamFilterHint" : "Settings_SpamFilterOffHint"];
        FilterOptions.Opacity = enabled ? 1 : 0.45;
        FilterOptions.InputTransparent = !enabled;
    }

    private void OnFilterToggled(object? sender, ToggledEventArgs e)
    {
        if (_binding || _settings is null)
            return;

        _settings.Enabled = e.Value;
        ShowFilterState();
    }

    private void ShowSensitivity(int percent)
    {
        var text = SpamItem.FormatPercent(percent);
        SensitivityValue.Text = text;
        SensitivityExplain.Text = string.Format(LocalizationManager.Instance["Settings_SensitivityExplain"], text);
        SettingsUi.Select(Resources, _presets.Select(p => p.Segment), _presets.FirstOrDefault(p => p.Percent == percent).Segment);
    }

    private void SaveThreshold(int percent)
    {
        if (_settings is not null && percent != ThresholdPercent)
            _settings.Threshold = percent / 100f;
    }

    private void OnSensitivityChanged(object? sender, ValueChangedEventArgs e)
    {
        if (_binding)
            return;

        var percent = (int)Math.Round(e.NewValue);
        ShowSensitivity(percent);
        SaveThreshold(percent);
    }

    private void OnPresetTapped(object? sender, TappedEventArgs e)
    {
        var preset = _presets.FirstOrDefault(p => p.Segment == sender);
        if (preset.Segment is null)
            return;

        SaveThreshold(preset.Percent);
        _binding = true;
        SensitivitySlider.Value = preset.Percent;
        _binding = false;
        ShowSensitivity(preset.Percent);
    }

    private async void OnAutoClearTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            var loc = LocalizationManager.Instance;
            var labels = RetentionChoices.Select(SettingsUi.Days).ToArray();
            var choice = await Shell.Current.DisplayActionSheetAsync(loc["Settings_AutoClearTitle"], loc["Chat_Cancel"], null, labels);
            var index = Array.IndexOf(labels, choice);
            if (index < 0 || RetentionChoices[index] == RetentionDays)
                return;

            if (_spam is not null)
                await _spam.SetRetentionDaysAsync(RetentionChoices[index]);
            else if (_settings is not null)
                _settings.RetentionDays = RetentionChoices[index];
            AutoClearValue.Text = SettingsUi.Days(RetentionDays);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Changing spam auto-clear failed: {ex}");
        }
    }

    private void OnTrustedTapped(object? sender, TappedEventArgs e) => RequestOpen(SettingsScreenKind.Trusted);

    private void OnAnalyticsTapped(object? sender, TappedEventArgs e) => RequestOpen(SettingsScreenKind.Analytics);

    // TODO: download a newer model through ISpamModelUpdater.InstallAsync once there is a server to get it from.
    private void OnModelUpdateTapped(object? sender, TappedEventArgs e) =>
        Toast.Show(LocalizationManager.Instance["Settings_ComingSoon"]);
}
