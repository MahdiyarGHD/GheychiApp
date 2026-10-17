namespace Gheychi.Core.Folders;

/// <summary>A folder that sorts the inbox by a rule instead of by what the user put in it.</summary>
public enum SmartFolder
{
    Unread,
    Contacts,
    Unknown,
    Senders,
    Codes,
    Failed,
    Drafts,
    Pinned
}

/// <summary>
/// A tab of the inbox. A custom folder holds the conversations the user put in it; a smart one (<see cref="Smart"/> set)
/// holds whatever its rule matches and has no name of its own, the app words it.
/// </summary>
public sealed record Folder(string Id, string Name, string IconKey, SmartFolder? Smart = null)
{
    /// <summary>The folder that shows everything. It is not stored: it appears when there is another folder, and is always first.</summary>
    public const string AllId = "all";

    public bool IsSmart => Smart is not null;
}

/// <summary>What the smart folders look at in a conversation.</summary>
public readonly record struct ThreadFacts(
    bool IsUnread,
    bool IsContact,
    bool IsSender,
    bool HasCode,
    bool HasFailed,
    bool HasDraft,
    bool IsPinned);
