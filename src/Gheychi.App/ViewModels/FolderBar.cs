using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Media;
using Gheychi.App.Localization;
using Gheychi.App.Theming;
using Gheychi.App.Ui;
using Gheychi.Core.Folders;

namespace Gheychi.App.ViewModels;

/// <summary>One tab of the inbox's folder bar.</summary>
public sealed class FolderPill : INotifyPropertyChanged
{
    private static readonly IBrush InactiveFill = Palette.Pick("#E3E6EA", "#26292E");
    private static readonly IBrush InactiveText = Palette.Pick("#3B3D40", "#C9CED4");
    private static readonly IBrush ActiveText = Palette.Brush("#FFFFFF");
    private static readonly IBrush ActiveBadgeFill = Palette.Brush("#40FFFFFF");

    private bool _isActive;
    private string _unreadText = string.Empty;

    public FolderPill(Folder folder)
    {
        Id = folder.Id;
        Title = Describe(folder);
        Icon = IconCatalog.Find(IconFile(folder));
        IsSmart = folder.IsSmart;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; }

    public string Title { get; }

    public Geometry? Icon { get; }

    public bool IsSmart { get; }

    public bool IsAll => Id == Folder.AllId;

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value)
                return;

            _isActive = value;
            Notify(nameof(IsActive), nameof(Fill), nameof(TextBrush), nameof(Font), nameof(BadgeFill));
        }
    }

    /// <summary>The unread count as the tab shows it, or empty when the tab shows none.</summary>
    public string UnreadText
    {
        get => _unreadText;
        set
        {
            if (_unreadText == value)
                return;

            _unreadText = value;
            Notify(nameof(UnreadText), nameof(HasUnread));
        }
    }

    public bool HasUnread => _unreadText.Length > 0;

    public IBrush Fill => _isActive ? Palette.Accent(AccentRole.Solid) : InactiveFill;

    public IBrush TextBrush => _isActive ? ActiveText : InactiveText;

    public FontFamily Font => _isActive ? AppFonts.Bold : AppFonts.Regular;

    public IBrush BadgeFill => _isActive ? ActiveBadgeFill : Palette.Accent(AccentRole.Solid);

    public static string Describe(Folder folder)
    {
        var loc = LocalizationManager.Instance;
        if (folder.Id == Folder.AllId)
            return loc["Folder_All"];

        return folder.Smart is { } smart ? loc["Folder_Smart_" + smart] : folder.Name;
    }

    /// <summary>The icon name <see cref="IconCatalog"/> looks up: the folder's own pick, or the one its rule has.</summary>
    public static string IconFile(Folder folder)
    {
        if (folder.Id == Folder.AllId)
            return "folder_general";

        return folder.Smart switch
        {
            SmartFolder.Unread => "folder_unread",
            SmartFolder.Contacts => "folder_people",
            SmartFolder.Unknown => "folder_unknown",
            SmartFolder.Senders => "folder_chat",
            SmartFolder.Codes => "folder_code",
            SmartFolder.Failed => "folder_failed",
            SmartFolder.Drafts => "folder_drafts",
            SmartFolder.Pinned => "pin",
            _ => "folder_" + folder.IconKey
        };
    }

    private void Notify(params string[] names)
    {
        foreach (var name in names)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>The row of folder tabs above the inbox. Empty, and so hidden, until the user makes a folder.</summary>
public sealed class FolderBar : INotifyPropertyChanged
{
    private bool _isVisible;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<FolderPill> Pills { get; } = [];

    public bool IsVisible
    {
        get => _isVisible;
        private set
        {
            if (_isVisible == value)
                return;

            _isVisible = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible)));
        }
    }

    /// <summary>
    /// Brings the tabs in line with <paramref name="folders"/> (without "All", which this adds in front when there is a
    /// folder) and <paramref name="unread"/>, the unread count of each folder by id. Tabs that stay are kept, so the bar
    /// does not blink on every refresh.
    /// </summary>
    public void Show(IReadOnlyList<Folder> folders, string activeId, IReadOnlyDictionary<string, int> unread, bool showCounts, bool barOn)
    {
        var wanted = folders.Count == 0
            ? []
            : new[] { new Folder(Folder.AllId, string.Empty, FolderRules.DefaultIcon) }.Concat(folders).ToList();

        var titles = wanted.Select(FolderPill.Describe).ToList();
        var same = Pills.Count == wanted.Count;
        for (var i = 0; same && i < wanted.Count; i++)
        {
            var pill = Pills[i];
            same = pill.Id == wanted[i].Id && pill.Title == titles[i] && pill.IsSmart == wanted[i].IsSmart &&
                   ReferenceEquals(pill.Icon, Ui.IconCatalog.Find(FolderPill.IconFile(wanted[i])));
        }

        if (!same)
        {
            Pills.Clear();
            foreach (var folder in wanted)
                Pills.Add(new FolderPill(folder));
        }

        foreach (var pill in Pills)
        {
            pill.IsActive = pill.Id == activeId;
            var count = unread.GetValueOrDefault(pill.Id);
            pill.UnreadText = showCounts && count > 0 ? FolderRules.CountText(count) : string.Empty;
        }

        IsVisible = barOn && Pills.Count > 0;
    }
}
