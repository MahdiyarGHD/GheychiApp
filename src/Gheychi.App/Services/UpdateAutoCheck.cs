using Gheychi.Core.Spam;

namespace Gheychi.App.Services;

/// <summary>
/// Looks for a newer spam model each time the app comes to the front. <see cref="SpamModelUpdates"/> still asks the
/// source at most once per <see cref="SpamModelUpdates.CheckInterval"/>; this only keeps a source that cannot be
/// reached from being asked again on every return to the app.
/// </summary>
internal static class SpamModelAutoCheck
{
    // Out of the way of the first inbox frames: a check that finds a release loads the model to compare versions.
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

            if (IPlatformApplication.Current?.Services.GetService<SpamModelUpdates>() is not { } updates)
                return;

            await Task.Delay(StartDelay);
            _lastAttemptTick = Environment.TickCount64;
            await Task.Run(() => updates.CheckAsync(force: false));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Automatic spam model check failed: {ex}");
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }
}
