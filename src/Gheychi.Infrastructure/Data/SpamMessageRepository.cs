using Gheychi.Core.Spam;
using SQLite;

namespace Gheychi.Infrastructure.Data;

public sealed class SpamMessageRepository : ISpamMessageRepository
{
    private readonly SQLiteAsyncConnection _db;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public SpamMessageRepository(string databasePath)
    {
        var dir = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        _db = new SQLiteAsyncConnection(databasePath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);
    }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized)
            return;

        await _initLock.WaitAsync();
        try
        {
            if (!_initialized)
            {
                await _db.CreateTableAsync<SpamMessageEntity>();
                _initialized = true;
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<long> AddAsync(SpamMessage message)
    {
        try
        {
            await EnsureInitializedAsync();
            var entity = new SpamMessageEntity
            {
                Address = message.Address,
                Body = message.Body,
                TimestampMillis = new DateTimeOffset(message.Timestamp).ToUnixTimeMilliseconds(),
                SubId = message.SubId,
                Score = message.Score,
                ModelVersion = message.ModelVersion
            };
            await _db.InsertAsync(entity);
            return entity.Id;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Storing spam failed: {ex}");
            return 0;
        }
    }

    public async Task<IReadOnlyList<SpamMessage>> GetAllAsync()
    {
        try
        {
            await EnsureInitializedAsync();
            var entities = await _db.Table<SpamMessageEntity>()
                .OrderByDescending(x => x.TimestampMillis)
                .ToListAsync();

            return entities.Select(MapToDomain).ToList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Reading spam failed: {ex}");
            return Array.Empty<SpamMessage>();
        }
    }

    public async Task DeleteAsync(IEnumerable<long> ids)
    {
        try
        {
            await EnsureInitializedAsync();
            foreach (var chunk in ids.Distinct().Chunk(400))
            {
                var placeholders = string.Join(",", chunk.Select(_ => "?"));
                await _db.ExecuteAsync($"DELETE FROM SpamMessage WHERE Id IN ({placeholders})", chunk.Cast<object>().ToArray());
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Deleting spam failed: {ex}");
        }
    }

    public async Task DeleteAllAsync()
    {
        try
        {
            await EnsureInitializedAsync();
            await _db.DeleteAllAsync<SpamMessageEntity>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Deleting spam failed: {ex}");
        }
    }

    public async Task DeleteOlderThanAsync(DateTime cutoff)
    {
        try
        {
            await EnsureInitializedAsync();
            var cutoffMillis = new DateTimeOffset(cutoff).ToUnixTimeMilliseconds();
            await _db.ExecuteAsync("DELETE FROM SpamMessage WHERE TimestampMillis < ?", cutoffMillis);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Deleting old spam failed: {ex}");
        }
    }

    private static SpamMessage MapToDomain(SpamMessageEntity entity) =>
        new(
            entity.Id,
            entity.Address,
            entity.Body,
            DateTimeOffset.FromUnixTimeMilliseconds(entity.TimestampMillis).LocalDateTime,
            entity.SubId,
            entity.Score,
            entity.ModelVersion);
}
