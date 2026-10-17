using System.Text.Json;

namespace Gheychi.Core.Folders;

/// <summary>
/// The user's folders and what they put in them. In memory and without a storage of its own: whoever holds it saves the
/// text from <see cref="Serialize"/> after a change that returned true.
/// </summary>
public sealed class FolderBook
{
    private readonly List<Folder> _folders = [];
    private readonly Dictionary<string, HashSet<long>> _members = [];

    private sealed record Saved(List<Folder>? Folders, Dictionary<string, List<long>>? Members);

    /// <summary>In the order of the tabs, without the "All" tab.</summary>
    public IReadOnlyList<Folder> Folders => _folders;

    public Folder? Find(string id) => _folders.Find(f => f.Id == id);

    public bool Has(SmartFolder smart) => _folders.Exists(f => f.Smart == smart);

    public Folder Add(string name, string iconKey)
    {
        var folder = new Folder(Guid.NewGuid().ToString("N"), FolderRules.NormalizeName(name), FolderRules.IconOrDefault(iconKey));
        _folders.Add(folder);
        return folder;
    }

    /// <summary>Adds a smart folder at the end; null when it is already there.</summary>
    public Folder? AddSmart(SmartFolder smart)
    {
        if (Has(smart))
            return null;

        var folder = new Folder("smart-" + smart.ToString().ToLowerInvariant(), string.Empty, FolderRules.DefaultIcon, smart);
        _folders.Add(folder);
        return folder;
    }

    /// <summary>Renames a custom folder and changes its icon; smart folders keep theirs.</summary>
    public bool Update(string id, string name, string iconKey)
    {
        var index = _folders.FindIndex(f => f.Id == id && !f.IsSmart);
        var normalized = FolderRules.NormalizeName(name);
        if (index < 0 || normalized.Length == 0)
            return false;

        var updated = _folders[index] with { Name = normalized, IconKey = FolderRules.IconOrDefault(iconKey) };
        if (updated == _folders[index])
            return false;

        _folders[index] = updated;
        return true;
    }

    public bool Remove(string id)
    {
        var removed = _folders.RemoveAll(f => f.Id == id) > 0;
        if (removed)
            _members.Remove(id);
        return removed;
    }

    /// <summary>Moves a folder by <paramref name="offset"/> places; false at either end.</summary>
    public bool Move(string id, int offset)
    {
        var from = _folders.FindIndex(f => f.Id == id);
        var to = from + offset;
        if (from < 0 || to < 0 || to >= _folders.Count || offset == 0)
            return false;

        var folder = _folders[from];
        _folders.RemoveAt(from);
        _folders.Insert(to, folder);
        return true;
    }

    public IReadOnlySet<long> Members(string folderId) =>
        _members.TryGetValue(folderId, out var members) ? members : EmptyMembers;

    private static readonly HashSet<long> EmptyMembers = [];

    /// <summary>The custom folders a conversation is in.</summary>
    public IReadOnlyList<string> FoldersOf(long threadId) =>
        _folders.Where(f => !f.IsSmart && Members(f.Id).Contains(threadId)).Select(f => f.Id).ToList();

    /// <summary>Puts conversations into a custom folder or takes them out; true when anything changed.</summary>
    public bool SetMembership(IEnumerable<long> threadIds, string folderId, bool member)
    {
        if (_folders.Find(f => f.Id == folderId) is not { IsSmart: false })
            return false;

        var changed = false;
        foreach (var id in threadIds.Where(id => id > 0))
        {
            if (member)
            {
                if (!_members.TryGetValue(folderId, out var set))
                    _members[folderId] = set = [];
                changed |= set.Add(id);
            }
            else if (_members.TryGetValue(folderId, out var set))
            {
                changed |= set.Remove(id);
            }
        }

        return changed;
    }

    public string Serialize() => JsonSerializer.Serialize(new Saved(
        _folders.ToList(),
        _members.Where(m => m.Value.Count > 0).ToDictionary(m => m.Key, m => m.Value.ToList())));

    public static FolderBook Load(string? json)
    {
        var book = new FolderBook();
        if (string.IsNullOrWhiteSpace(json))
            return book;

        try
        {
            if (JsonSerializer.Deserialize<Saved>(json) is not { } saved)
                return book;

            foreach (var folder in saved.Folders ?? [])
            {
                if (string.IsNullOrEmpty(folder.Id) || book.Find(folder.Id) is not null)
                    continue;

                if (folder.IsSmart)
                    book.AddSmart(folder.Smart!.Value);
                else if (FolderRules.NormalizeName(folder.Name) is { Length: > 0 } name)
                    book._folders.Add(folder with { Name = name, IconKey = FolderRules.IconOrDefault(folder.IconKey) });
            }

            foreach (var (folderId, ids) in saved.Members ?? [])
            {
                if (book.Find(folderId) is { IsSmart: false })
                    book._members[folderId] = [.. ids.Where(id => id > 0)];
            }
        }
        catch (JsonException)
        {
            return new FolderBook();
        }

        return book;
    }
}
