namespace Gheychi.Core.Folders;

public static class FolderRules
{
    public const int MaxNameLength = 24;
    public const int MaxCount = 99;

    /// <summary>The icons a folder can wear; the keys name the app's <c>Icon.Folder*</c> shapes. The first is the default.</summary>
    public static IReadOnlyList<string> IconKeys { get; } =
        ["general", "star", "work", "home", "people", "bank", "shop", "heart", "bookmark", "school", "travel", "chat"];

    public static string DefaultIcon => IconKeys[0];

    public static string IconOrDefault(string? key) =>
        key is not null && IconKeys.Contains(key) ? key : DefaultIcon;

    /// <summary>The name as it is kept: trimmed, one line, cut to <see cref="MaxNameLength"/>; empty when there is nothing left.</summary>
    public static string NormalizeName(string? name)
    {
        var flat = (name ?? string.Empty).ReplaceLineEndings(" ").Trim();
        return flat.Length <= MaxNameLength ? flat : flat[..MaxNameLength].TrimEnd();
    }

    /// <summary>Whether the conversation belongs under <paramref name="folder"/>; <paramref name="isMember"/> is the user's own choice.</summary>
    public static bool Matches(Folder folder, ThreadFacts facts, bool isMember)
    {
        if (folder.Id == Folder.AllId)
            return true;

        return folder.Smart switch
        {
            SmartFolder.Unread => facts.IsUnread,
            SmartFolder.Contacts => facts.IsContact && !facts.IsSender,
            SmartFolder.Unknown => !facts.IsContact && !facts.IsSender,
            SmartFolder.Senders => facts.IsSender,
            SmartFolder.Codes => facts.HasCode,
            SmartFolder.Failed => facts.HasFailed,
            SmartFolder.Drafts => facts.HasDraft,
            SmartFolder.Pinned => facts.IsPinned,
            _ => isMember
        };
    }

    /// <summary>How many conversations are unread, as the tab shows it: 99+ beyond ninety-nine.</summary>
    public static string CountText(int unread) => unread > MaxCount ? $"{MaxCount}+" : unread.ToString();
}
