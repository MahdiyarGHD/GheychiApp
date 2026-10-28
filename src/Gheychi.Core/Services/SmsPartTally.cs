namespace Gheychi.Core.Services;

/// <summary>
/// Combines the per-part results of one outgoing message. A long message goes out as several parts and the radio
/// answers for each of them; the message is accepted only when every part was, and it has failed as soon as any part has.
/// </summary>
public sealed class SmsPartTally
{
    private readonly int _expected;
    private int _received;

    public SmsPartTally(int parts) => _expected = Math.Max(1, parts);

    /// <summary>False once any reported part failed.</summary>
    public bool AllOk { get; private set; } = true;

    /// <summary>True when every part has been reported.</summary>
    public bool IsComplete => _received >= _expected;

    /// <summary>Counts one part's result; true when that was the last one the message was waiting for.</summary>
    public bool Record(bool ok)
    {
        _received++;
        AllOk &= ok;
        return IsComplete;
    }
}
