using System.Text.Json;
using Gheychi.Core.Models;
using Gheychi.Core.Services;

namespace Gheychi.App.Services;

/// <summary>
/// Unsent drafts, kept in the app preferences. Held in memory and written through: the inbox reads them on every refresh.
/// </summary>
public sealed class PreferencesDraftStore : IDraftStore
{
    private const string Key = "message_drafts_v1";

    private readonly object _gate = new();
    private readonly Dictionary<long, MessageDraft> _drafts = [];
    private int _version;

    public PreferencesDraftStore()
    {
        try
        {
            var raw = Preferences.Default.Get(Key, string.Empty);
            if (!string.IsNullOrEmpty(raw) && JsonSerializer.Deserialize(raw, AppJsonContext.Default.ListMessageDraft) is { } saved)
            {
                foreach (var draft in saved.Where(d => !string.IsNullOrWhiteSpace(d.Text)))
                    _drafts[draft.ThreadId] = draft;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Reading the drafts failed: {ex}");
        }
    }

    public int Version
    {
        get
        {
            lock (_gate)
                return _version;
        }
    }

    public MessageDraft? Get(long threadId)
    {
        lock (_gate)
            return _drafts.GetValueOrDefault(threadId);
    }

    public IReadOnlyCollection<MessageDraft> GetAll()
    {
        lock (_gate)
            return _drafts.Values.ToList();
    }

    public void Save(MessageDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.Text))
        {
            Remove(draft.ThreadId);
            return;
        }

        lock (_gate)
        {
            if (_drafts.TryGetValue(draft.ThreadId, out var known) && known.Text == draft.Text)
                return;

            _drafts[draft.ThreadId] = draft;
            Persist();
        }
    }

    public void Remove(long threadId) => Remove([threadId]);

    public void Remove(IEnumerable<long> threadIds)
    {
        lock (_gate)
        {
            var removed = false;
            foreach (var id in threadIds)
                removed |= _drafts.Remove(id);

            if (removed)
                Persist();
        }
    }

    private void Persist()
    {
        _version++;
        try
        {
            Preferences.Default.Set(Key, JsonSerializer.Serialize(_drafts.Values.ToList(), AppJsonContext.Default.ListMessageDraft));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Saving the drafts failed: {ex}");
        }
    }
}
