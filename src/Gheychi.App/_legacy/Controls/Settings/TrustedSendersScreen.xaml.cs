using Gheychi.App.ViewModels;
using Gheychi.Core.Services;

namespace Gheychi.App.Controls.Settings;

public partial class TrustedSendersScreen : SettingsScreen
{
    private readonly SpamViewModel? _spam;

    public TrustedSendersScreen()
    {
        InitializeComponent();
        _spam = IPlatformApplication.Current?.Services.GetService<SpamViewModel>();
    }

    public override void OnShown() =>
        TrustedList.ItemsSource = (_spam?.TrustedSenders() ?? [])
            .Select(key => new TrustedSenderRow(key, PhoneNumberNormalizer.FormatDisplay(key)))
            .OrderBy(row => row.Display, StringComparer.CurrentCulture)
            .ToList();

    private void OnRemoveTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Element)?.BindingContext is not TrustedSenderRow row || _spam is null)
            return;

        _spam.SetTrusted(row.Key, false);
        OnShown();
    }
}
