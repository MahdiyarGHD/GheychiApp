using SQLite;

namespace Gheychi.Infrastructure.Data;

[Table("MessageMetadata")]
public sealed class MessageMetadataEntity
{
    [PrimaryKey]
    public long MessageId { get; set; }

    [Indexed]
    public long ThreadId { get; set; }

    [Indexed]
    public bool IsStarred { get; set; }

    public string? Reaction { get; set; }

    public bool ReactionFromMe { get; set; }

    public DateTime? ReactionTime { get; set; }

    public DateTime UpdatedAt { get; set; }
}
