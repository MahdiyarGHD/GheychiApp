global using Microsoft.Extensions.DependencyInjection;
using Android.App;
using Android.Content;

namespace Gheychi.App;

/// <summary>The Android handles the rest of the app needs, set by <see cref="MainActivity"/> and <see cref="AndroidApp"/>.</summary>
public static class Platform
{
    public static Context AppContext => Application.Context;

    /// <summary>The visible activity, or null while the app is in the background.</summary>
    public static Activity? CurrentActivity { get; internal set; }
}

/// <summary>The app's services, once the Avalonia application has built them.</summary>
public interface IPlatformApplication
{
    public static IPlatformApplication? Current { get; internal set; }

    IServiceProvider Services { get; }
}

internal sealed class PlatformApplication(IServiceProvider services) : IPlatformApplication
{
    public IServiceProvider Services { get; } = services;
}
