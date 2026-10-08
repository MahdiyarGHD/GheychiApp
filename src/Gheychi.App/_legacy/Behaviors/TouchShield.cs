using System.ComponentModel;

namespace Gheychi.App.Behaviors;

/// <summary>
/// Makes a full-screen overlay swallow the touches it does not handle itself. Without it Android
/// hands a tap on an empty part of the overlay (its header, the gap between rows) to whatever sits
/// underneath, so the inbox row or search bar behind the overlay reacts.
/// </summary>
public static class TouchShield
{
    public static readonly BindableProperty IsEnabledProperty =
        BindableProperty.CreateAttached(
            "IsEnabled",
            typeof(bool),
            typeof(TouchShield),
            false,
            propertyChanged: OnIsEnabledChanged);

    public static bool GetIsEnabled(BindableObject view) => (bool)view.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(BindableObject view, bool value) => view.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not VisualElement view)
            return;

        view.HandlerChanged -= OnHandlerChanged;
        view.PropertyChanged -= OnPropertyChanged;
        if ((bool)newValue)
        {
            view.HandlerChanged += OnHandlerChanged;
            view.PropertyChanged += OnPropertyChanged;
            Apply(view);
        }
    }

    private static void OnHandlerChanged(object? sender, EventArgs e)
    {
        if (sender is VisualElement view)
            Apply(view);
    }

    // Toggling InputTransparent (parking / showing the overlay) can reset the native flag.
    private static void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VisualElement.InputTransparent) && sender is VisualElement view)
            view.Dispatcher.Dispatch(() => Apply(view));
    }

    public static void Apply(VisualElement view)
    {
#if ANDROID
        if (view.Handler?.PlatformView is Android.Views.View native)
            native.Clickable = true;
#endif
    }
}
