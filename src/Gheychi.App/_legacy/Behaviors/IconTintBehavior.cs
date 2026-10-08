using System.ComponentModel;
using Android.Graphics;
using Android.Widget;
using Microsoft.Maui.Controls;
using Color = Microsoft.Maui.Graphics.Color;

namespace Gheychi.App.Behaviors;

public sealed class IconTintBehavior : Behavior<Image>
{
    public static readonly BindableProperty TintColorProperty =
        BindableProperty.Create(
            nameof(TintColor),
            typeof(Color),
            typeof(IconTintBehavior),
            null,
            propertyChanged: static (bindable, _, _) => ((IconTintBehavior)bindable).Apply());

    private Image? _image;
    private int? _lastAppliedColor;
    private ImageView? _lastImageView;

    public Color TintColor
    {
        get => (Color)GetValue(TintColorProperty)!;
        set => SetValue(TintColorProperty, value);
    }

    protected override void OnAttachedTo(Image bindable)
    {
        base.OnAttachedTo(bindable);
        _image = bindable;
        BindingContext = bindable.BindingContext;
        bindable.BindingContextChanged += OnBindingContextChanged;
        bindable.Loaded += OnLoaded;
        bindable.HandlerChanged += OnHandlerChanged;
        bindable.PropertyChanged += OnPropertyChanged;
        Apply();
    }

    protected override void OnDetachingFrom(Image bindable)
    {
        bindable.BindingContextChanged -= OnBindingContextChanged;
        bindable.Loaded -= OnLoaded;
        bindable.HandlerChanged -= OnHandlerChanged;
        bindable.PropertyChanged -= OnPropertyChanged;
        _image = null;
        _lastAppliedColor = null;
        _lastImageView = null;
        base.OnDetachingFrom(bindable);
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        Apply();
    }

    private void OnBindingContextChanged(object? sender, EventArgs e)
    {
        if (_image != null)
        {
            BindingContext = _image.BindingContext;
            Apply();
        }
    }

    private void OnLoaded(object? sender, EventArgs e) => Apply();

    private void OnHandlerChanged(object? sender, EventArgs e) => Apply();

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == VisualElement.IsVisibleProperty.PropertyName)
        {
            Apply();
        }
        else if (e.PropertyName == Image.SourceProperty.PropertyName)
        {
            _lastAppliedColor = null;
            Apply();
        }
    }

    private void Apply()
    {
        if (_image == null || !_image.IsVisible)
            return;

        if (_image.Handler?.PlatformView is not ImageView imageView)
            return;

        if (TintColor is { } tint)
        {
            var a = (int)(tint.Alpha * 255);
            var r = (int)(tint.Red * 255);
            var g = (int)(tint.Green * 255);
            var b = (int)(tint.Blue * 255);
            var argb = (a << 24) | (r << 16) | (g << 8) | b;

            // Rows are re-bound constantly while scrolling; re-allocating a filter and posting a
            // runnable for an unchanged tint on the same native view is pure overhead.
            if (_lastAppliedColor == argb && ReferenceEquals(_lastImageView, imageView))
                return;

            _lastAppliedColor = argb;
            _lastImageView = imageView;
            var color = Android.Graphics.Color.Argb(a, r, g, b);
            imageView.SetColorFilter(new PorterDuffColorFilter(color, PorterDuff.Mode.SrcIn!));
            imageView.Post(() =>
            {
                if (_image?.Handler?.PlatformView is ImageView iv)
                    iv.SetColorFilter(new PorterDuffColorFilter(color, PorterDuff.Mode.SrcIn!));
            });
        }
        else
        {
            if (_lastAppliedColor == null)
                return;

            _lastAppliedColor = null;
            _lastImageView = null;
            imageView.ClearColorFilter();
        }
    }
}
