using Gheychi.App.ViewModels;

namespace Gheychi.App.Pages;

public partial class ChatPage : ContentPage
{
    private bool _popping;

    public ChatPage(ThreadItem thread)
    {
        InitializeComponent();
        ApplyTopInsets();
        ChatContent.TranslationY = GetOffscreenY();
        ChatContent.Bind(thread);
        ChatContent.BackRequested += OnChatBack;
    }

    private void ApplyTopInsets()
    {
        var top = GetStatusBarHeight();
        Padding = new Thickness(0, top, 0, 0);
    }

    private static double GetStatusBarHeight()
    {
#if ANDROID
        var activity = Platform.CurrentActivity;
        var decorView = activity?.Window?.DecorView;
        if (decorView is not null)
        {
            var insets = AndroidX.Core.View.ViewCompat.GetRootWindowInsets(decorView);
            var statusBars = insets?.GetInsets(AndroidX.Core.View.WindowInsetsCompat.Type.StatusBars());
            if (statusBars is not null && statusBars.Top > 0)
            {
                var density = DeviceDisplay.Current.MainDisplayInfo.Density;
                if (density > 0)
                    return statusBars.Top / density;
            }
        }
        return 32;
#else
        return 0;
#endif
    }

    private double GetOffscreenY()
    {
        if (Height > 0)
            return Height + 20;
        var density = DeviceDisplay.Current.MainDisplayInfo.Density;
        if (density > 0)
            return (DeviceDisplay.Current.MainDisplayInfo.Height / density) + 50;
        return 850;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _popping = false;
        ChatContent.TranslationY = GetOffscreenY();
        try
        {
            await ChatContent.TranslateToAsync(0, 0, 240, Easing.CubicOut);
        }
        catch (InvalidOperationException)
        {
            ChatContent.TranslationY = 0;
        }
    }

    private async Task OnChatBack() => await PopOnce();

    protected override bool OnBackButtonPressed()
    {
        if (ChatContent.HandleBack())
            return true;
        MainThread.BeginInvokeOnMainThread(async () => await PopOnce());
        return true;
    }

    private async Task PopOnce()
    {
        if (_popping)
            return;
        _popping = true;
        try
        {
            await ChatContent.TranslateToAsync(0, GetOffscreenY(), 220, Easing.CubicIn);
        }
        catch (InvalidOperationException)
        {
        }
        await Navigation.PopAsync(animated: false);
    }
}
