using SQLite;

namespace Gheychi.Infrastructure.Data;

[Table("SpamMessage")]
public sealed class SpamMessageEntity
{
    [PrimaryKey, AutoIncrement]
    public long Id { get; set; }

    public string Address { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    [Indexed]
    public long TimestampMillis { get; set; }

    public int SubId { get; set; }

    public float Score { get; set; }

    public int ModelVersion { get; set; }

    public float Threshold { get; set; }

    [Indexed]
    public long ScanId { get; set; }

    public bool Unread { get; set; }
}
