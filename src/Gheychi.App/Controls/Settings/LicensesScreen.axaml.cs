using Avalonia.Controls;
using Avalonia.Input;
using Gheychi.App.ViewModels;

namespace Gheychi.App.Controls.Settings;

public partial class LicensesScreen : SettingsScreen
{
    private static readonly LicenseRow[] Licenses =
    [
        new("Avalonia", "MIT", "https://github.com/AvaloniaUI/Avalonia"),
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
        LicenseList.ItemsSource = Licenses;
    }

    private async void OnLicenseTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if ((sender as Control)?.DataContext is LicenseRow row)
                await Launcher.Default.OpenAsync(row.Url);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Opening a license failed: {ex}");
        }
    }
}
