using Gheychi.Core.Diagnostics;

namespace Gheychi.App.Services;

/// <summary>
/// Times each launch from the moment Android started the process to the first frame, and keeps the last runs in a
/// file. A phone without adb has no other way to time a cold start; Settings → About copies the report.
/// </summary>
internal static class StartupTiming
{
    // A process started for an incoming text that opens its window much later is not a launch worth timing. Generous,
    // because the launches this exists for are the slow ones.
    private const long ColdLaunchActivityLimitMs = 10_000;

    private static readonly StartupTrace Trace = new(
        global::Android.OS.Process.StartUptimeMillis, global::Android.OS.SystemClock.UptimeMillis);

    private static int _finished;

    private static string FilePath => Path.Combine(FileSystem.AppDataDirectory, "startup-trace.txt");

    public static void Mark(string name) => Trace.Mark(name);

    /// <summary>Called once the first frame is let through; writes the report on a worker thread.</summary>
    public static void Finish()
    {
        if (Interlocked.Exchange(ref _finished, 1) == 1)
            return;

        _ = Task.Run(() =>
        {
            try
            {
                if (Trace.ElapsedAt("activity.oncreate") is not { } activity || activity > ColdLaunchActivityLimitMs)
                    return;

                var loaded = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).Order();
                var report = Trace.Format(DateTime.Now) + "assemblies: " + string.Join(", ", loaded) + "\n";
                var history = File.Exists(FilePath) ? File.ReadAllText(FilePath) : string.Empty;
                File.WriteAllText(FilePath, StartupHistory.Prepend(history, report));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Startup timing failed: {ex}");
            }
        });
    }

    public static string ReadHistory() => File.Exists(FilePath) ? File.ReadAllText(FilePath) : string.Empty;
}
