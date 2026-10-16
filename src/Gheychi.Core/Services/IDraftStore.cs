using Gheychi.Core.Models;

namespace Gheychi.Core.Services;

public interface IDraftStore
{
    /// <summary>Counts every change, so a list built from the drafts can tell that it is out of date.</summary>
    int Version { get; }

    MessageDraft? Get(long threadId);

    IReadOnlyCollection<MessageDraft> GetAll();

    /// <summary>Keeps the draft of its conversation; a draft with no text is the same as none.</summary>
    void Save(MessageDraft draft);

    void Remove(long threadId);

    void Remove(IEnumerable<long> threadIds);
}
