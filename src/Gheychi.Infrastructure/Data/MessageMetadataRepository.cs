using Gheychi.Core.Models;
using Gheychi.Core.Services;
using SQLite;

namespace Gheychi.Infrastructure.Data;

public sealed class MessageMetadataRepository : IMessageMetadataRepository
{
    private readonly SQLiteAsyncConnection _db;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public MessageMetadataRepository(string? databasePath = null)
    {
        var path = databasePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "gheychi.db");

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _db = new SQLiteAsyncConnection(path, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);
    }

    public MessageMetadataRepository(SQLiteAsyncConnection connection)
    {
        _db = connection;
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
                await _db.CreateTableAsync<MessageMetadataEntity>();
                _initialized = true;
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<MessageMetadata?> GetAsync(long messageId)
    {
        try
        {
            await EnsureInitializedAsync();
            var entity = await _db.Table<MessageMetadataEntity>()
                .Where(x => x.MessageId == messageId)
                .FirstOrDefaultAsync();

            return entity is null ? null : MapToDomain(entity);
        }
        catch
        {
            return null;
        }
    }

    public async Task<IReadOnlyDictionary<long, MessageMetadata>> GetForMessagesAsync(IEnumerable<long> messageIds)
    {
        try
        {
            await EnsureInitializedAsync();
            var idList = messageIds.Distinct().ToList();
            if (idList.Count == 0)
                return new Dictionary<long, MessageMetadata>();

            var entities = await _db.Table<MessageMetadataEntity>()
                .Where(x => idList.Contains(x.MessageId))
                .ToListAsync();

            return entities.ToDictionary(e => e.MessageId, MapToDomain);
        }
        catch
        {
            return new Dictionary<long, MessageMetadata>();
        }
    }

    public async Task<IReadOnlyList<MessageMetadata>> GetStarredForThreadAsync(long threadId)
    {
        try
        {
            await EnsureInitializedAsync();
            var entities = await _db.Table<MessageMetadataEntity>()
                .Where(x => x.ThreadId == threadId && x.IsStarred)
                .ToListAsync();

            return entities.Select(MapToDomain).ToList();
        }
        catch
        {
            return Array.Empty<MessageMetadata>();
        }
    }

    public async Task<IReadOnlyList<long>> GetAllStarredMessageIdsAsync()
    {
        try
        {
            await EnsureInitializedAsync();
            var entities = await _db.Table<MessageMetadataEntity>()
                .Where(x => x.IsStarred)
                .ToListAsync();

            return entities.Select(e => e.MessageId).ToList();
        }
        catch
        {
            return Array.Empty<long>();
        }
    }

    public async Task SetStarredAsync(long messageId, long threadId, bool isStarred)
    {
        try
        {
            await EnsureInitializedAsync();
            var entity = await _db.Table<MessageMetadataEntity>()
                .Where(x => x.MessageId == messageId)
                .FirstOrDefaultAsync();

            if (entity is null)
            {
                entity = new MessageMetadataEntity
                {
                    MessageId = messageId,
                    ThreadId = threadId,
                    IsStarred = isStarred,
                    UpdatedAt = DateTime.UtcNow
                };
                await _db.InsertAsync(entity);
            }
            else
            {
                entity.IsStarred = isStarred;
                entity.UpdatedAt = DateTime.UtcNow;
                await _db.UpdateAsync(entity);
            }
        }
        catch
        {
        }
    }

    public async Task SetReactionAsync(long messageId, long threadId, string? emoji, bool fromMe)
    {
        try
        {
            await EnsureInitializedAsync();
            var entity = await _db.Table<MessageMetadataEntity>()
                .Where(x => x.MessageId == messageId)
                .FirstOrDefaultAsync();

            if (entity is null)
            {
                entity = new MessageMetadataEntity
                {
                    MessageId = messageId,
                    ThreadId = threadId,
                    Reaction = emoji,
                    ReactionFromMe = fromMe,
                    ReactionTime = emoji is null ? null : DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await _db.InsertAsync(entity);
            }
            else
            {
                entity.Reaction = emoji;
                entity.ReactionFromMe = fromMe;
                entity.ReactionTime = emoji is null ? null : DateTime.UtcNow;
                entity.UpdatedAt = DateTime.UtcNow;
                await _db.UpdateAsync(entity);
            }
        }
        catch
        {
        }
    }

    public async Task DeleteAsync(IEnumerable<long> messageIds)
    {
        try
        {
            await EnsureInitializedAsync();
            var idList = messageIds.Distinct().ToList();
            if (idList.Count == 0)
                return;

            // One statement per chunk instead of one transaction (and fsync) per id.
            foreach (var chunk in idList.Chunk(400))
            {
                var placeholders = string.Join(",", chunk.Select(_ => "?"));
                await _db.ExecuteAsync($"DELETE FROM MessageMetadata WHERE MessageId IN ({placeholders})", chunk.Cast<object>().ToArray());
            }
        }
        catch
        {
        }
    }

    public async Task DeleteForThreadsAsync(IEnumerable<long> threadIds)
    {
        try
        {
            await EnsureInitializedAsync();
            var idList = threadIds.Distinct().ToList();
            if (idList.Count == 0)
                return;

            var placeholders = string.Join(",", idList.Select(_ => "?"));
            await _db.ExecuteAsync($"DELETE FROM MessageMetadata WHERE ThreadId IN ({placeholders})", idList.Cast<object>().ToArray());
        }
        catch
        {
        }
    }

    private static MessageMetadata MapToDomain(MessageMetadataEntity entity) =>
        new()
        {
            MessageId = entity.MessageId,
            ThreadId = entity.ThreadId,
            IsStarred = entity.IsStarred,
            Reaction = entity.Reaction,
            ReactionFromMe = entity.ReactionFromMe,
            ReactionTime = entity.ReactionTime
        };
}
