namespace Gheychi.Core.Models;

/// <summary>A message deleted from the phone's SMS store, kept for a while so it can be put back.</summary>
/// <param name="Type">The store's own type of the row (inbox, sent, failed...).</param>
/// <param name="DateMs">When the message was sent or received, as the store had it.</param>
/// <param name="TrashedAtMs">When it was deleted. Messages deleted together share this value.</param>
public sealed record TrashedMessage(long Id, string Address, string Body, long DateMs, int Type, int SubId, long TrashedAtMs);
