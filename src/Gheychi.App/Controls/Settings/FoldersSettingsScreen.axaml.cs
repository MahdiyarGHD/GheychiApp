using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Gheychi.App.Localization;
using Gheychi.App.Services;
using Gheychi.App.Theming;
using Gheychi.App.Ui;
using Gheychi.App.ViewModels;
using Gheychi.Core.Folders;

namespace Gheychi.App.Controls.Settings;

public partial class FoldersSettingsScreen : SettingsScreen
{
    private static PreferencesFolderStore? Store => IPlatformApplication.Current?.Services.GetService<PreferencesFolderStore>();

    private bool _binding;

    public FoldersSettingsScreen()
    {
        InitializeComponent();
    }

    public override void OnShown()
    {
        _binding = true;
        BarSwitch.IsChecked = AppPreferences.ShowFolderBar;
        CountsSwitch.IsChecked = AppPreferences.ShowFolderCounts;
        RememberSwitch.IsChecked = AppPreferences.RememberLastFolder;
        _binding = false;
        Rebuild();
    }

    private void Rebuild()
    {
        if (Store is not { } store)
            return;

        var loc = LocalizationManager.Instance;
        var folders = store.Folders;

        FolderList.Children.Clear();
        foreach (var folder in folders)
        {
            if (FolderList.Children.Count > 0)
                FolderList.Children.Add(Divider());

            var subtitle = folder.IsSmart
                ? loc["Folder_SmartHint_" + folder.Smart]
                : string.Format(loc["Folder_Conversations"], store.Members(folder.Id).Count);
            var row = Row(FolderPill.IconFile(folder), FolderPill.Describe(folder), subtitle, Chevron());
            var id = folder.Id;
            row.Tapped += (_, _) => OnFolderTapped(id);
            FolderList.Children.Add(row);
        }

        if (folders.Count > 0)
            FolderList.Children.Add(Divider());
        else
            FolderList.Children.Add(Hint(loc["Folder_NoneYet"]));
        FolderList.Children.Add(Divider());
        var add = Row("folder_add", loc["Folder_New"], string.Empty, null, accent: true);
        add.Tapped += (_, _) => OpenEditor(null);
        FolderList.Children.Add(add);

        SmartList.Children.Clear();
        foreach (var smart in Enum.GetValues<SmartFolder>())
        {
            if (SmartList.Children.Count > 0)
                SmartList.Children.Add(Divider());

            var folder = new Folder("smart-" + smart.ToString().ToLowerInvariant(), string.Empty, FolderRules.DefaultIcon, smart);
            var toggle = new ToggleButton { IsChecked = store.Has(smart) };
            toggle.Classes.Add("setting");
            var kind = smart;
            toggle.IsCheckedChanged += (_, _) =>
            {
                if (_binding)
                    return;

                if (toggle.IsChecked == true)
                    store.AddSmart(kind);
                else
                    store.Remove(folder.Id);
                Rebuild();
            };
            SmartList.Children.Add(Row(FolderPill.IconFile(folder), FolderPill.Describe(folder), loc["Folder_SmartHint_" + smart], toggle));
        }
    }

    private async void OnFolderTapped(string id)
    {
        try
        {
            if (Store is not { } store || store.Find(id) is not { } folder)
                return;

            var loc = LocalizationManager.Instance;
            var edit = loc["Folder_Edit"];
            var up = loc["Folder_MoveUp"];
            var down = loc["Folder_MoveDown"];
            var delete = loc["Folder_Delete"];

            var options = new List<string>();
            if (!folder.IsSmart)
                options.Add(edit);
            var index = store.Folders.ToList().FindIndex(f => f.Id == id);
            if (index > 0)
                options.Add(up);
            if (index < store.Folders.Count - 1)
                options.Add(down);

            var choice = await Dialogs.ActionSheetAsync(FolderPill.Describe(folder), loc["Chat_Cancel"], delete, [.. options]);
            if (choice == edit)
                OpenEditor(id);
            else if (choice == up)
                store.Move(id, -1);
            else if (choice == down)
                store.Move(id, 1);
            else if (choice == delete && await Confirm(loc["Folder_DeleteTitle"], loc["Folder_DeleteMessage"], delete))
                store.Remove(id);

            Rebuild();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Folder action failed: {ex}");
        }
    }

    private void OpenEditor(string? id)
    {
        FolderEditScreen.EditingId = id;
        RequestOpen(SettingsScreenKind.FolderEdit);
    }

    private static void RefreshInbox() =>
        IPlatformApplication.Current?.Services.GetService<MessagesViewModel>()?.RefreshFolders();

    private void OnBarToggled(object? sender, RoutedEventArgs e)
    {
        if (_binding)
            return;

        AppPreferences.ShowFolderBar = BarSwitch.IsChecked == true;
        RefreshInbox();
    }

    private void OnCountsToggled(object? sender, RoutedEventArgs e)
    {
        if (_binding)
            return;

        AppPreferences.ShowFolderCounts = CountsSwitch.IsChecked == true;
        RefreshInbox();
    }

    private void OnRememberToggled(object? sender, RoutedEventArgs e)
    {
        if (!_binding)
            AppPreferences.RememberLastFolder = RememberSwitch.IsChecked == true;
    }

    // ---- Rows, built like the XAML ones -----------------------------------------------------------

    private static Border Divider()
    {
        var divider = new Border();
        divider.Classes.Add("divider");
        return divider;
    }

    private static Control Chevron()
    {
        var chevron = new Icon { Data = IconCatalog.Find("chevron_right.png") };
        chevron.Classes.Add("chevron");
        return chevron;
    }

    private static TextBlock Hint(string text)
    {
        var hint = new TextBlock { Text = text, Margin = new Thickness(20, 16) };
        hint.Classes.Add("rowSubtitle");
        return hint;
    }

    private static Border Row(string iconFile, string title, string subtitle, Control? trailing, bool accent = false)
    {
        var badge = new Border
        {
            Background = Palette.Accent(accent ? AccentRole.Solid : AccentRole.Soft),
            Child = new Icon
            {
                Data = IconCatalog.Find(iconFile),
                Foreground = accent ? Palette.Brush("#FFFFFF") : Palette.Accent(AccentRole.Text)
            }
        };
        badge.Classes.Add("badge");
        ((Icon)badge.Child).Classes.Add("badgeIcon");

        var titleText = new TextBlock { Text = title };
        titleText.Classes.Add("rowTitle");
        var texts = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { titleText } };
        Grid.SetColumn(texts, 1);
        if (subtitle.Length > 0)
        {
            var subtitleText = new TextBlock { Text = subtitle };
            subtitleText.Classes.Add("rowSubtitle");
            texts.Children.Add(subtitleText);
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 14 };
        grid.Children.Add(badge);
        grid.Children.Add(texts);
        if (trailing is not null)
        {
            Grid.SetColumn(trailing, 2);
            grid.Children.Add(trailing);
        }

        var row = new Border { Child = grid, Background = Palette.Transparent };
        row.Classes.Add("row");
        return row;
    }
}
