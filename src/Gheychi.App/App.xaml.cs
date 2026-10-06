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
		return new Window(new AppShell());
	}

	private void ApplyFontResources()
	{
		var isPersian = CultureInfo.CurrentUICulture.Name.StartsWith("fa", StringComparison.OrdinalIgnoreCase);

		Resources["AppFontFamily"] = isPersian ? "Vazirmatn" : "PlusJakartaSans";
		Resources["AppFontFamilyBold"] = isPersian ? "VazirmatnSemiBold" : "PlusJakartaSansSemiBold";
	}
}
