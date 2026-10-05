namespace Gheychi.App.Gestures;

/// <summary>
/// When the user last touched the screen. Background warm-up work that must run on the UI thread
/// (building screens, filling caches) waits for a quiet moment instead of freezing a scroll or a tap.
/// </summary>
public static class UserActivity
{
    private static long _lastTouchTick;

    public static void Touched() => Volatile.Write(ref _lastTouchTick, Environment.TickCount64);

    public static bool IsIdle(int quietMilliseconds) =>
        Environment.TickCount64 - Volatile.Read(ref _lastTouchTick) >= quietMilliseconds;

    public static async Task WaitForIdleAsync(int quietMilliseconds = 700, CancellationToken cancellationToken = default)
    {
        while (!IsIdle(quietMilliseconds))
            await Task.Delay(150, cancellationToken);
    }
}
