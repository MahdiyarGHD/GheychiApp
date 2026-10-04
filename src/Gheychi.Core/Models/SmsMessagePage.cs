using System.Collections;

namespace Gheychi.Core.Models;

/// <summary>
/// A page of messages plus how many provider rows it was read from. Reaction SMS are folded into
/// the message they react to, so the list can be shorter than the page; paging (offset, "is there
/// more") has to count provider rows, not list items.
/// </summary>
public sealed class SmsMessagePage(List<SmsMessage> messages, int rawCount) : IReadOnlyList<SmsMessage>
{
    public int RawCount { get; } = rawCount;

    public int Count => messages.Count;

    public SmsMessage this[int index] => messages[index];

    public IEnumerator<SmsMessage> GetEnumerator() => messages.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public static int RawCountOf(IReadOnlyList<SmsMessage> messages) =>
        messages is SmsMessagePage page ? page.RawCount : messages.Count;
}
