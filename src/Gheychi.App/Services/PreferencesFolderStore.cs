using Gheychi.Core.Folders;

namespace Gheychi.App.Services;

/// <summary>
/// The inbox folders and what is in them, kept in the app preferences. Held in memory and written through: the inbox reads
/// them on every refresh. Everything handed out is a copy, so a screen never sees the book change under it.
/// </summary>
public sealed class PreferencesFolderStore
{
    private const string Key = "folders_v1";

    private readonly object _gate = new();
    private readonly FolderBook _book;
    private int _version;

    public PreferencesFolderStore()
    {
        string? raw = null;
        try
        {
            raw = Preferences.Default.Get(Key, string.Empty);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Reading the folders failed: {ex}");
        }

        _book = FolderBook.Load(raw);
    }

    /// <summary>Raised after any change, from whichever thread made it.</summary>
    public event Action? Changed;

    /// <summary>Counts every change, so a list built from the folders can tell that it is out of date.</summary>
    public int Version
    {
        get
        {
            lock (_gate)
                return _version;
        }
    }

    public IReadOnlyList<Folder> Folders
    {
        get
        {
            lock (_gate)
                return _book.Folders.ToList();
        }
    }

    public Folder? Find(string id)
    {
        lock (_gate)
            return _book.Find(id);
    }

    public bool Has(SmartFolder smart)
    {
        lock (_gate)
            return _book.Has(smart);
    }

    public IReadOnlySet<long> Members(string folderId)
    {
        lock (_gate)
            return new HashSet<long>(_book.Members(folderId));
    }

    public IReadOnlyList<string> FoldersOf(long threadId)
    {
        lock (_gate)
            return _book.FoldersOf(threadId);
    }

    /// <summary>The folders every one of <paramref name="threadIds"/> is in, which is what a checkbox for several conversations shows.</summary>
    public IReadOnlySet<string> FoldersOfAll(IReadOnlyCollection<long> threadIds)
    {
        lock (_gate)
        {
            var common = threadIds.Count == 0
                ? new HashSet<string>()
                : new HashSet<string>(_book.FoldersOf(threadIds.First()));
            foreach (var id in threadIds.Skip(1))
                common.IntersectWith(_book.FoldersOf(id));
            return common;
        }
    }

    public Folder Add(string name, string iconKey)
    {
        Folder? added = null;
        Edit(book => (added = book.Add(name, iconKey)) is not null);
        return added!;
    }

    public bool AddSmart(SmartFolder smart) => Edit(book => book.AddSmart(smart) is not null);

    public bool Update(string id, string name, string iconKey) => Edit(book => book.Update(id, name, iconKey));

    public bool Remove(string id) => Edit(book => book.Remove(id));

    public bool Move(string id, int offset) => Edit(book => book.Move(id, offset));

    public bool SetMembership(IEnumerable<long> threadIds, string folderId, bool member)
    {
        var ids = threadIds.ToList();
        return Edit(book => book.SetMembership(ids, folderId, member));
    }

    private bool Edit(Func<FolderBook, bool> change)
    {
        lock (_gate)
        {
            if (!change(_book))
                return false;

            _version++;
            try
            {
                Preferences.Default.Set(Key, _book.Serialize());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Saving the folders failed: {ex}");
            }
        }

        Changed?.Invoke();
        return true;
    }
}
