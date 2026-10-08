using Avalonia.Threading;

namespace Gheychi.App;

public static class MainThread
{
    public static bool IsMainThread => Dispatcher.UIThread.CheckAccess();

    /// <summary>Always queued, even on the UI thread: callers rely on the current call stack having unwound first.</summary>
    public static void BeginInvokeOnMainThread(Action action) => Dispatcher.UIThread.Post(action);

    public static Task InvokeOnMainThreadAsync(Action action) =>
        Dispatcher.UIThread.CheckAccess() ? RunInline(action) : Dispatcher.UIThread.InvokeAsync(action).GetTask();

    public static Task<T> InvokeOnMainThreadAsync<T>(Func<T> func) =>
        Dispatcher.UIThread.CheckAccess() ? Task.FromResult(func()) : Dispatcher.UIThread.InvokeAsync(func).GetTask();

    public static Task InvokeOnMainThreadAsync(Func<Task> func) =>
        Dispatcher.UIThread.CheckAccess() ? func() : Dispatcher.UIThread.InvokeAsync(func);

    public static Task<T> InvokeOnMainThreadAsync<T>(Func<Task<T>> func) =>
        Dispatcher.UIThread.CheckAccess() ? func() : Dispatcher.UIThread.InvokeAsync(func);

    private static Task RunInline(Action action)
    {
        action();
        return Task.CompletedTask;
    }
}
