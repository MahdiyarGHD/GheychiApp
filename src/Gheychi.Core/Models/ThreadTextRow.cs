namespace Gheychi.Core.Models;

/// <summary>
/// The text of one message in a conversation, newest first when listed. Its position in the list is its
/// offset from the newest message, which is what the chat's paging counts in.
/// </summary>
public sealed record ThreadTextRow(long Id, long DateMs, string Body);
