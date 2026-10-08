using Gheychi.App.Localization;
using Gheychi.App.ViewModels;

namespace Gheychi.App.Controls.Settings;

public partial class PrivacySettingsScreen : SettingsScreen
{
    private readonly SpamViewModel? _spam;

    public PrivacySettingsScreen()
    {
        InitializeComponent();
        _spam = IPlatformApplication.Current?.Services.GetService<SpamViewModel>();
    }

    public override void OnShown()
    {
        var loc = LocalizationManager.Instance;
        var count = _spam?.Count ?? 0;
        ClearSpamHint.Text = count == 0 ? loc["Settings_ClearSpamNone"] : string.Format(loc["Settings_ClearSpamHint"], SettingsUi.Number(count));
    }

    private void OnClearSearchesTapped(object? sender, TappedEventArgs e)
    {
        SearchViewModel.ClearSavedRecentSearches();
        Toast.Show(LocalizationManager.Instance["Settings_ClearSearchesDone"]);
    }

    private async void OnClearSpamTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            var loc = LocalizationManager.Instance;
            if (_spam is not { HasAny: true } || Confirm is null
                || !await Confirm(loc["Spam_ClearAllTitle"], loc["Spam_ClearAllMessage"], loc["Spam_ClearAll"]))
                return;

            await _spam.ClearAllAsync();
            OnShown();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Clearing spam from settings failed: {ex}");
        }
    }
}
