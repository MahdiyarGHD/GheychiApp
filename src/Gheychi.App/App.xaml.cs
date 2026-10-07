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
		window.Activated += (_, _) => _ = Services.UpdateAutoCheck.RunAsync();
		// The alarm is gone after a force stop, and an app update can leave it unset.
		_ = Task.Run(() => Platforms.Android.Notifications.SpamDigestNotifier.Schedule(Platform.AppContext));
		return window;
	}

	private void ApplyFontResources()
	{
		var isPersian = CultureInfo.CurrentUICulture.Name.StartsWith("fa", StringComparison.OrdinalIgnoreCase);

		Resources["AppFontFamily"] = isPersian ? "Vazirmatn" : "PlusJakartaSans";
		Resources["AppFontFamilyBold"] = isPersian ? "VazirmatnSemiBold" : "PlusJakartaSansSemiBold";

		// A 13sp digit centred by its line box is not centred by its ink: from the fonts' metrics, Plus Jakarta Sans
		// digits sit 0.12em below the middle and Vazirmatn digits 0.088em above it. Used to centre the SIM badge digit.
		Resources["BadgeDigitOffsetY"] = isPersian ? 1.1 : -1.6;
	}
}
