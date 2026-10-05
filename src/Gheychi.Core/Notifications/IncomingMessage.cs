namespace Gheychi.Core.Notifications;

/// <summary>A received SMS that is already stored, as seen by the notification rules.</summary>
public sealed record IncomingMessage(long ThreadId, string Address, string Body, long TimestampMillis, int SubId);
