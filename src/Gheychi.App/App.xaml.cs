using System.Globalization;

namespace Gheychi.App;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
		UserAppTheme = Services.AppPreferences.Theme;
		ApplyFontResources();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new AppShell());
		window.Activated += (_, _) => _ = Services.SpamModelAutoCheck.RunAsync();
		return window;
	}

	private void ApplyFontResources()
	{
		var isPersian = CultureInfo.CurrentUICulture.Name.StartsWith("fa", StringComparison.OrdinalIgnoreCase);

		Resources["AppFontFamily"] = isPersian ? "Vazirmatn" : "PlusJakartaSans";
		Resources["AppFontFamilyBold"] = isPersian ? "VazirmatnSemiBold" : "PlusJakartaSansSemiBold";
	}
}
