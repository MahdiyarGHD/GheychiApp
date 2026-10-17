using Android.Content;
using Android.Text;
using Android.Views;
using Android.Widget;
using Gheychi.App.Localization;
using Gheychi.App.Services;
using Gheychi.Core.Folders;
using AlertDialog = AndroidX.AppCompat.App.AlertDialog;

namespace Gheychi.App.Ui;

/// <summary>The system dialogs of the folders: ask for a name, and pick the folders of some conversations.</summary>
public static class FolderDialogs
{
    private static PreferencesFolderStore? Store => IPlatformApplication.Current?.Services.GetService<PreferencesFolderStore>();

    /// <summary>The name typed, cleaned up; null when the user cancelled or left it empty.</summary>
    public static Task<string?> AskNameAsync(string title, string initial = "")
    {
        var result = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (Platform.CurrentActivity is not { } activity)
            return Task.FromResult<string?>(null);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            var loc = LocalizationManager.Instance;
            var input = new EditText(activity)
            {
                Hint = loc["Folder_NameHint"],
                Text = initial,
                ImeOptions = global::Android.Views.InputMethods.ImeAction.Done
            };
            input.SetSingleLine(true);
            input.SetFilters([new InputFilterLengthFilter(FolderRules.MaxNameLength)]);
            input.SetSelection(input.Text?.Length ?? 0);

            var density = activity.Resources?.DisplayMetrics?.Density ?? 1f;
            var side = (int)(24 * density);
            var box = new FrameLayout(activity);
            box.SetPadding(side, (int)(8 * density), side, 0);
            box.AddView(input);

            var dialog = new AlertDialog.Builder(activity)
                .SetTitle(title)!
                .SetView(box)!
                .SetPositiveButton(loc["Folder_Save"], (_, _) =>
                    result.TrySetResult(FolderRules.NormalizeName(input.Text) is { Length: > 0 } name ? name : null))!
                .SetNegativeButton(loc["Chat_Cancel"], (_, _) => result.TrySetResult(null))!
                .Create()!;
            dialog.DismissEvent += (_, _) => result.TrySetResult(null);
            dialog.Window?.SetSoftInputMode(SoftInput.StateVisible);
            dialog.Show();
            Dialogs.TintButtons(dialog);
            input.RequestFocus();
        });
        return result.Task;
    }

    /// <summary>
    /// The folders of the given conversations as checkboxes, and a way to make a new one. A box is ticked when every one of
    /// the conversations is in the folder; ticking or clearing it puts all of them in or takes all of them out.
    /// </summary>
    public static Task PickFoldersAsync(IReadOnlyCollection<long> threadIds)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (Store is not { } store || threadIds.Count == 0 || Platform.CurrentActivity is not { } activity)
        {
            done.TrySetResult();
            return done.Task;
        }

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                var loc = LocalizationManager.Instance;
                var folders = store.Folders.Where(f => !f.IsSmart).ToList();
                if (folders.Count == 0)
                {
                    await CreateAndAddAsync(store, threadIds);
                    return;
                }

                var initial = store.FoldersOfAll(threadIds);
                var ticked = folders.Select(f => initial.Contains(f.Id)).ToArray();
                var createNew = false;

                var picked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var dialog = new AlertDialog.Builder(activity)
                    .SetTitle(loc["Folder_AddTo"])!
                    .SetMultiChoiceItems(folders.Select(f => f.Name).ToArray(), (bool[])ticked.Clone(), (_, e) => ticked[e.Which] = e.IsChecked)!
                    .SetPositiveButton(loc["Folder_Save"], (_, _) => picked.TrySetResult(true))!
                    .SetNeutralButton(loc["Folder_New"], (_, _) =>
                    {
                        createNew = true;
                        picked.TrySetResult(true);
                    })!
                    .SetNegativeButton(loc["Chat_Cancel"], (_, _) => picked.TrySetResult(false))!
                    .Create()!;
                dialog.DismissEvent += (_, _) => picked.TrySetResult(false);
                dialog.Show();
                Dialogs.TintButtons(dialog);

                if (!await picked.Task)
                    return;

                for (var i = 0; i < folders.Count; i++)
                {
                    if (ticked[i] != initial.Contains(folders[i].Id))
                        store.SetMembership(threadIds, folders[i].Id, ticked[i]);
                }

                if (createNew)
                    await CreateAndAddAsync(store, threadIds);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Choosing folders failed: {ex}");
            }
            finally
            {
                done.TrySetResult();
            }
        });
        return done.Task;
    }

    private static async Task CreateAndAddAsync(PreferencesFolderStore store, IReadOnlyCollection<long> threadIds)
    {
        if (await AskNameAsync(LocalizationManager.Instance["Folder_New"]) is not { } name)
            return;

        var folder = store.Add(name, FolderRules.DefaultIcon);
        store.SetMembership(threadIds, folder.Id, true);
    }
}
