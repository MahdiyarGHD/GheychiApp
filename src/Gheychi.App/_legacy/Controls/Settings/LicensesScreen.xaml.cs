using Gheychi.App.ViewModels;

namespace Gheychi.App.Controls.Settings;

public partial class LicensesScreen : SettingsScreen
{
    private static readonly LicenseRow[] Licenses =
    [
        new(".NET MAUI", "MIT", "https://github.com/dotnet/maui"),
        new("ML.NET", "MIT", "https://github.com/dotnet/machinelearning"),
        new("sqlite-net", "MIT", "https://github.com/praeclarum/sqlite-net"),
        new("SQLitePCLRaw", "Apache 2.0", "https://github.com/ericsink/SQLitePCL.raw"),
        new("SQLite", "Public domain", "https://sqlite.org/copyright.html"),
        new("Lucide / Feather icons", "ISC / MIT", "https://lucide.dev/license"),
        new("Plus Jakarta Sans", "SIL Open Font License 1.1", "https://github.com/tokotype/PlusJakartaSans"),
        new("Vazirmatn", "SIL Open Font License 1.1", "https://github.com/rastikerdar/vazirmatn")
    ];

    public LicensesScreen()
    {
        InitializeComponent();
        BindableLayout.SetItemsSource(LicenseList, Licenses);
    }

    private async void OnLicenseTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if ((sender as Element)?.BindingContext is LicenseRow row)
                await Launcher.Default.OpenAsync(row.Url);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Opening a license failed: {ex}");
        }
    }
}
