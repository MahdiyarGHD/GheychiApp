using Gheychi.Core.Spam;
using Gheychi.Core.Updates;

namespace Gheychi.App.Services;

/// <summary>
/// Looks for a newer app version and spam model each time the app comes to the front. <see cref="AppUpdates"/> and
/// <see cref="SpamModelUpdates"/> still ask their source at most once per check interval; this only keeps a source
/// that cannot be reached from being asked again on every return to the app.
/// </summary>
internal static class UpdateAutoCheck
{
    // Out of the way of the first inbox frames: a model check that finds a release loads the model to compare versions.
    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(15);

    private static long? _lastAttemptTick;
    private static int _running;

    public static async Task RunAsync()
    {
        if (Interlocked.Exchange(ref _running, 1) == 1)
            return;

        try
        {
            if (_lastAttemptTick is { } last && Environment.TickCount64 - last < RetryAfter.TotalMilliseconds)
                return;

            var services = IPlatformApplication.Current?.Services;
            if (services is null)
                return;

            await Task.Delay(StartDelay);
            _lastAttemptTick = Environment.TickCount64;

            // One failing does not keep the other from being checked.
            if (services.GetService<AppUpdates>() is { } app)
                await CheckAsync("app", () => app.CheckAsync(force: false));
            if (services.GetService<SpamModelUpdates>() is { } model)
                await CheckAsync("spam model", () => model.CheckAsync(force: false));
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    private static async Task CheckAsync(string what, Func<Task> check)
    {
        try
        {
            await Task.Run(check);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Automatic {what} update check failed: {ex}");
        }
    }
}
