using Android.App;
using Android.App.Job;
using Android.Content;
using Gheychi.App.Platforms.Android.Notifications;
using Gheychi.App.Services;
using Gheychi.Core.Updates;

namespace Gheychi.App.Platforms.Android.Services;

/// <summary>
/// Looks for an app update once a day, also while the app is not used, and posts a notification for a new version.
/// A job and not an alarm: the system runs it once there is a network, batched with other apps' work.
/// </summary>
[Service(Name = "com.evergreen.gheychiapp.UpdateCheckJob", Permission = "android.permission.BIND_JOB_SERVICE", Exported = true)]
public class UpdateCheckJob : JobService
{
    private const int JobId = 7402;

    private CancellationTokenSource? _cancel;

    /// <summary>Puts the daily check on the system's schedule, or takes it off when automatic checks are off.</summary>
    public static void Schedule(Context context)
    {
        try
        {
            if (context.GetSystemService(Context.JobSchedulerService) is not JobScheduler scheduler)
                return;

            if (!AppPreferences.AutoUpdateCheck || IPlatformApplication.Current?.Services.GetService<AppUpdates>() is null)
            {
                scheduler.Cancel(JobId);
                return;
            }

            // Scheduling it again would restart its day.
            if (scheduler.GetPendingJob(JobId) is not null)
                return;

            var job = new JobInfo.Builder(JobId, new ComponentName(context, Java.Lang.Class.FromType(typeof(UpdateCheckJob))))
                .SetRequiredNetworkType(NetworkType.Any)!
                .SetPeriodic((long)TimeSpan.FromDays(1).TotalMilliseconds)!
                .SetPersisted(true)!
                .Build();
            scheduler.Schedule(job!);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Scheduling the update check failed: {ex}");
        }
    }

    public override bool OnStartJob(JobParameters? parameters)
    {
        var cancel = _cancel = new CancellationTokenSource();
        var context = ApplicationContext ?? this;
        _ = Task.Run(async () =>
        {
            try
            {
                if (AppPreferences.AutoUpdateCheck
                    && IPlatformApplication.Current?.Services.GetService<AppUpdates>() is { } updates
                    && await updates.CheckAsync(force: false, cancel.Token) is { } release
                    // On screen, the Settings badge already says so.
                    && !ChatPresence.IsAppVisible)
                    UpdateNotifier.Show(context, release);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Background update check failed: {ex}");
            }
            finally
            {
                JobFinished(parameters, false);
            }
        });
        return true;
    }

    public override bool OnStopJob(JobParameters? parameters)
    {
        _cancel?.Cancel();
        return true;
    }
}
