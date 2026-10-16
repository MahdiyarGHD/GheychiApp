using SQLite;

namespace Gheychi.Infrastructure.Data;

[Table("TrashMessage")]
public sealed class TrashMessageEntity
{
    [PrimaryKey, AutoIncrement]
    public long Id { get; set; }

    public string Address { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public long DateMs { get; set; }

    public int Type { get; set; }

    public int SubId { get; set; }

    [Indexed]
    public long TrashedAtMs { get; set; }
}
