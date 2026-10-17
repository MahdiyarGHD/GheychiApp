namespace Gheychi.Core.Models;

/// <summary>
/// Text typed into a conversation and not sent. The address and name are kept so a conversation with no message yet
/// (a new one) can still be listed.
/// </summary>
public sealed record MessageDraft(long ThreadId, string Address, string Name, int SubId, string Text, long DateMs);
