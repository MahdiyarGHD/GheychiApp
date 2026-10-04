namespace Gheychi.Core.Models;

public sealed class MessageMetadata
{
    public long MessageId { get; set; }
    public long ThreadId { get; set; }
    public bool IsStarred { get; set; }
    public string? Reaction { get; set; }
    public bool ReactionFromMe { get; set; }
    public DateTime? ReactionTime { get; set; }
}
