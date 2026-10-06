using SQLite;

namespace Gheychi.Infrastructure.Data;

[Table("SpamStat")]
public sealed class SpamStatEntity
{
    [PrimaryKey, AutoIncrement]
    public long Id { get; set; }

    [Indexed]
    public long SpamMessageId { get; set; }

    public string SenderKey { get; set; } = string.Empty;

    public long TimestampMillis { get; set; }

    public float Score { get; set; }

    public int ModelVersion { get; set; }

    public bool Restored { get; set; }
}
