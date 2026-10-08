using Android.App;
using Android.Content.PM;
using AndroidX.Core.App;
using AndroidX.Core.Content;

namespace Gheychi.App;

public enum PermissionStatus
{
    Unknown,
    Denied,
    Granted
}

public abstract class BasePlatformPermission
{
    public abstract (string androidPermission, bool isRuntime)[] RequiredPermissions { get; }
}

public static class Permissions
{
    private const int FirstRequestCode = 8100;

    private static readonly Dictionary<int, TaskCompletionSource<bool>> Pending = [];
    private static int _nextRequestCode = FirstRequestCode;

    public static Task<PermissionStatus> CheckStatusAsync<T>() where T : BasePlatformPermission, new() =>
        Task.FromResult(Missing(new T()).Count == 0 ? PermissionStatus.Granted : PermissionStatus.Denied);

    public static async Task<PermissionStatus> RequestAsync<T>() where T : BasePlatformPermission, new()
    {
        var missing = Missing(new T());
        if (missing.Count == 0)
            return PermissionStatus.Granted;

        if (Platform.CurrentActivity is not { } activity)
            return PermissionStatus.Denied;

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int code;
        lock (Pending)
        {
            code = _nextRequestCode++;
            Pending[code] = completion;
        }

        await MainThread.InvokeOnMainThreadAsync(() => ActivityCompat.RequestPermissions(activity, [.. missing], code));
        await completion.Task;
        return Missing(new T()).Count == 0 ? PermissionStatus.Granted : PermissionStatus.Denied;
    }

    public static bool ShouldShowRationale<T>() where T : BasePlatformPermission, new() =>
        Platform.CurrentActivity is { } activity
        && Missing(new T()).Any(permission => ActivityCompat.ShouldShowRequestPermissionRationale(activity, permission));

    /// <summary>Called by the activity with the answer to a request made through <see cref="RequestAsync{T}"/>.</summary>
    internal static void OnRequestPermissionsResult(int requestCode)
    {
        TaskCompletionSource<bool>? completion;
        lock (Pending)
            Pending.Remove(requestCode, out completion);
        completion?.TrySetResult(true);
    }

    private static List<string> Missing(BasePlatformPermission permission) =>
        permission.RequiredPermissions
            .Where(p => p.isRuntime && ContextCompat.CheckSelfPermission(Platform.AppContext, p.androidPermission) != Permission.Granted)
            .Select(p => p.androidPermission)
            .ToList();
}
