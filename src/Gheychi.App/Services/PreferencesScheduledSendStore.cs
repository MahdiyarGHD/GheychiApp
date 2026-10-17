using System.Text.Json;
using Gheychi.Core.Scheduling;

namespace Gheychi.App.Services;

/// <summary>
/// The messages waiting to be sent, kept in the app preferences. Held in memory and written through; the receiver that
/// the alarm starts reads the same instance, so a claim made there is seen by the chat at once.
/// </summary>
public sealed class PreferencesScheduledSendStore : IScheduledSendStore
{
    private const string Key = "scheduled_sends_v1";

    private readonly object _gate = new();
    private readonly List<ScheduledMessage> _messages = [];

    public PreferencesScheduledSendStore()
    {
        try
        {
            var raw = Preferences.Default.Get(Key, string.Empty);
            if (!string.IsNullOrEmpty(raw) && JsonSerializer.Deserialize<List<ScheduledMessage>>(raw) is { } saved)
                _messages.AddRange(saved.Where(m => !string.IsNullOrWhiteSpace(m.Text) && !string.IsNullOrEmpty(m.Id)));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Reading the scheduled messages failed: {ex}");
        }
    }

    public event Action? Changed;

    public IReadOnlyList<ScheduledMessage> GetAll()
    {
        lock (_gate)
            return _messages.OrderBy(m => m.SendAtMillis).ToList();
    }

    public IReadOnlyList<ScheduledMessage> ForThread(long threadId)
    {
        lock (_gate)
            return _messages.Where(m => m.ThreadId == threadId).OrderBy(m => m.SendAtMillis).ToList();
    }

    public void Add(ScheduledMessage message)
    {
        lock (_gate)
        {
            _messages.RemoveAll(m => m.Id == message.Id);
            _messages.Add(message);
            Persist();
        }

        Changed?.Invoke();
    }

    public ScheduledMessage? Claim(string id)
    {
        ScheduledMessage? claimed;
        lock (_gate)
        {
            claimed = _messages.Find(m => m.Id == id);
            if (claimed is null)
                return null;

            _messages.Remove(claimed);
            Persist();
        }

        Changed?.Invoke();
        return claimed;
    }

    private void Persist()
    {
        try
        {
            Preferences.Default.Set(Key, JsonSerializer.Serialize(_messages));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Saving the scheduled messages failed: {ex}");
        }
    }
}
