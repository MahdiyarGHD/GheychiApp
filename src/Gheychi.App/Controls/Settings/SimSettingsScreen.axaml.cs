using Avalonia.Input;
using Gheychi.App.Localization;
using Gheychi.App.Services;
using Gheychi.App.Ui;
using Gheychi.App.ViewModels;
using Gheychi.Core.Models;
using Gheychi.Core.Services;

namespace Gheychi.App.Controls.Settings;

public partial class SimSettingsScreen : SettingsScreen
{
    private readonly ISmsService? _sms;
    private IReadOnlyList<SimCardInfo> _sims = [];

    public SimSettingsScreen()
    {
        InitializeComponent();
        _sms = IPlatformApplication.Current?.Services.GetService<ISmsService>();
    }

    public override void OnShown() => _ = LoadAsync();

    private async Task LoadAsync()
    {
        try
        {
            if (_sms is null)
                return;

            _sims = (await _sms.GetActiveSimsAsync()).OrderBy(s => s.SlotIndex).ToList();
            var noName = LocalizationManager.Instance["Settings_SimNoName"];
            SimList.ItemsSource = _sims
                .Select(s => new SimSettingsRow(Title(s), string.IsNullOrWhiteSpace(s.DisplayName) ? noName : s.DisplayName))
                .ToList();
            ShowDefaultSim();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Reading SIM cards for settings failed: {ex}");
        }
    }

    private static string Title(SimCardInfo sim) => $"SIM {SettingsUi.Number(sim.SlotIndex)}";

    private static string Label(SimCardInfo sim) =>
        string.IsNullOrWhiteSpace(sim.DisplayName) ? Title(sim) : $"{Title(sim)} · {sim.DisplayName}";

    // With no choice saved, a new message starts on the first SIM.
    private SimCardInfo? DefaultSim() =>
        _sims.FirstOrDefault(s => s.SubId == AppPreferences.DefaultSubId) ?? _sims.FirstOrDefault();

    private void ShowDefaultSim() => DefaultSimValue.Text = DefaultSim() is { } sim ? Title(sim) : string.Empty;

    private async void OnDefaultSimTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (_sims.Count < 2)
                return;

            var loc = LocalizationManager.Instance;
            var labels = _sims.Select(Label).ToArray();
            var choice = await Dialogs.ActionSheetAsync(loc["Settings_DefaultSim"], loc["Chat_Cancel"], null, labels);
            var index = Array.IndexOf(labels, choice);
            if (index < 0)
                return;

            AppPreferences.DefaultSubId = _sims[index].SubId;
            ShowDefaultSim();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Choosing the default SIM failed: {ex}");
        }
    }
}
