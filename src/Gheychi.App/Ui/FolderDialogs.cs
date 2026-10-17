using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Gheychi.App.Localization;
using Gheychi.App.Services;
using Gheychi.App.Theming;
using Gheychi.App.ViewModels;
using Gheychi.Core.Folders;

namespace Gheychi.App.Ui;

/// <summary>The sheets of the folders: ask for a name, and pick the folders of some conversations.</summary>
public static class FolderDialogs
{
    private static PreferencesFolderStore? Store => IPlatformApplication.Current?.Services.GetService<PreferencesFolderStore>();

    /// <summary>The name typed, cleaned up; null when the user cancelled or left it empty.</summary>
    public static async Task<string?> AskNameAsync(string title, string initial = "")
    {
        var loc = LocalizationManager.Instance;
        var typed = await Sheet.PromptAsync(title, null, initial, loc["Folder_NameHint"], loc["Folder_Save"], loc["Chat_Cancel"], FolderRules.MaxNameLength);
        return FolderRules.NormalizeName(typed) is { Length: > 0 } name ? name : null;
    }

    private enum Outcome
    {
        Save,
        New,
        Dismissed
    }

    /// <summary>
    /// The folders of the given conversations as checkboxes, and a way to make a new one. A box is ticked when every one of
    /// the conversations is in the folder; ticking or clearing it puts all of them in or takes all of them out.
    /// </summary>
    public static async Task PickFoldersAsync(IReadOnlyCollection<long> threadIds)
    {
        if (Store is not { } store || threadIds.Count == 0)
            return;

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

            var outcome = await Sheet.ShowAsync(finish => BuildPicker(loc, folders, ticked, finish), Outcome.Dismissed);
            if (outcome == Outcome.Dismissed)
                return;

            for (var i = 0; i < folders.Count; i++)
            {
                if (ticked[i] != initial.Contains(folders[i].Id))
                    store.SetMembership(threadIds, folders[i].Id, ticked[i]);
            }

            if (outcome == Outcome.New)
                await CreateAndAddAsync(store, threadIds);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Choosing folders failed: {ex}");
        }
    }

    private static Control BuildPicker(LocalizationManager loc, List<Folder> folders, bool[] ticked, Action<Outcome> finish)
    {
        var rows = new StackPanel();
        for (var i = 0; i < folders.Count; i++)
        {
            var index = i;
            var check = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(2),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new Icon
                {
                    Data = IconCatalog.Find("check.png"),
                    Width = 14,
                    Height = 14,
                    Foreground = Palette.Brush("#FFFFFF"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            void Show()
            {
                check.Background = ticked[index] ? Palette.Accent(AccentRole.Solid) : Palette.Transparent;
                check.BorderBrush = ticked[index] ? Palette.Accent(AccentRole.Solid) : Sheet.Muted;
                check.Child!.IsVisible = ticked[index];
            }

            Show();
            var icon = new Icon
            {
                Data = IconCatalog.Find(FolderPill.IconFile(folders[i])),
                Width = 20,
                Height = 20,
                Foreground = Palette.Accent(AccentRole.Text),
                VerticalAlignment = VerticalAlignment.Center
            };
            var name = Sheet.Label(FolderPill.Describe(folders[i]), 16, Sheet.Ink);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.TextWrapping = TextWrapping.NoWrap;
            name.VerticalAlignment = VerticalAlignment.Center;

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 14 };
            Grid.SetColumn(icon, 1);
            Grid.SetColumn(name, 2);
            grid.Children.Add(check);
            grid.Children.Add(icon);
            grid.Children.Add(name);
            rows.Children.Add(Sheet.Tappable(grid, () =>
            {
                ticked[index] = !ticked[index];
                Show();
            }));
        }

        var add = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 14 };
        var plus = new Icon
        {
            Data = IconCatalog.Find("folder_add.png"),
            Width = 24,
            Height = 24,
            Foreground = Sheet.Accent,
            VerticalAlignment = VerticalAlignment.Center
        };
        var addText = Sheet.Label(loc["Folder_New"], 16, Sheet.Accent, bold: true);
        addText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(addText, 1);
        add.Children.Add(plus);
        add.Children.Add(addText);

        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        stack.Children.Add(Sheet.Grabber());
        stack.Children.Add(Sheet.Heading(loc["Folder_AddTo"]));
        stack.Children.Add(new ScrollViewer
        {
            MaxHeight = Sheet.MaxBodyHeight(),
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
            Content = rows
        });
        stack.Children.Add(Sheet.Tappable(add, () => finish(Outcome.New)));
        stack.Children.Add(new Border
        {
            Margin = new Thickness(20, 8, 20, 0),
            Child = Sheet.Button(loc["Folder_Save"], filled: true, () => finish(Outcome.Save))
        });
        return stack;
    }

    private static async Task CreateAndAddAsync(PreferencesFolderStore store, IReadOnlyCollection<long> threadIds)
    {
        if (await AskNameAsync(LocalizationManager.Instance["Folder_New"]) is not { } name)
            return;

        var folder = store.Add(name, FolderRules.DefaultIcon);
        store.SetMembership(threadIds, folder.Id, true);
    }
}
