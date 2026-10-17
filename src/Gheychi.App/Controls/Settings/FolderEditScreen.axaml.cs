using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Gheychi.App.Localization;
using Gheychi.App.Services;
using Gheychi.App.Theming;
using Gheychi.App.Ui;
using Gheychi.Core.Folders;

namespace Gheychi.App.Controls.Settings;

/// <summary>Makes a folder or edits one: its name and its icon.</summary>
public partial class FolderEditScreen : SettingsScreen
{
    /// <summary>The folder to edit, set before the screen is opened; null makes a new one.</summary>
    public static string? EditingId { get; set; }

    private static PreferencesFolderStore? Store => IPlatformApplication.Current?.Services.GetService<PreferencesFolderStore>();

    private readonly List<(string Key, Border Tile, Icon Glyph)> _tiles = [];
    private string _icon = FolderRules.DefaultIcon;
    private string? _editing;

    public FolderEditScreen()
    {
        InitializeComponent();
        BuildTiles();
    }

    public override void OnShown()
    {
        var loc = LocalizationManager.Instance;
        _editing = EditingId;
        if (_editing is not null && Store?.Find(_editing) is { IsSmart: false } folder)
        {
            Header.Title = loc["Folder_EditTitle"];
            NameBox.Text = folder.Name;
            _icon = folder.IconKey;
            DeleteButton.IsVisible = true;
        }
        else
        {
            _editing = null;
            Header.Title = loc["Folder_New"];
            NameBox.Text = string.Empty;
            _icon = FolderRules.DefaultIcon;
            DeleteButton.IsVisible = false;
        }

        ShowIcon();
    }

    private void BuildTiles()
    {
        foreach (var key in FolderRules.IconKeys)
        {
            var glyph = new Icon
            {
                Data = IconCatalog.Find("folder_" + key),
                Width = 24,
                Height = 24,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var tile = new Border
            {
                Width = 52,
                Height = 52,
                Margin = new Thickness(5),
                CornerRadius = new CornerRadius(16),
                Background = Palette.Transparent,
                Tag = key,
                Child = glyph
            };
            tile.Tapped += (_, _) =>
            {
                _icon = key;
                ShowIcon();
            };
            IconGrid.Children.Add(tile);
            _tiles.Add((key, tile, glyph));
        }
    }

    private void ShowIcon()
    {
        foreach (var (key, tile, glyph) in _tiles)
        {
            var on = key == _icon;
            tile.Background = on ? Palette.Accent(AccentRole.Solid) : Palette.Accent(AccentRole.Soft);
            glyph.Foreground = on ? Palette.Brush("#FFFFFF") : Palette.Accent(AccentRole.Text);
        }
    }

    private void OnSaveTapped(object? sender, TappedEventArgs e)
    {
        if (Store is not { } store)
            return;

        var name = FolderRules.NormalizeName(NameBox.Text);
        if (name.Length == 0)
        {
            Toast.Show(LocalizationManager.Instance["Folder_NameRequired"]);
            return;
        }

        if (_editing is null)
            store.Add(name, _icon);
        else
            store.Update(_editing, name, _icon);
        RequestBack();
    }

    private async void OnDeleteTapped(object? sender, TappedEventArgs e)
    {
        if (Store is not { } store || _editing is null)
            return;

        var loc = LocalizationManager.Instance;
        if (!await Confirm(loc["Folder_DeleteTitle"], loc["Folder_DeleteMessage"], loc["Folder_Delete"]))
            return;

        store.Remove(_editing);
        RequestBack();
    }
}
