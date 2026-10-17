using Gheychi.Core.Models;
using Gheychi.Core.Services;
using SQLite;

namespace Gheychi.Infrastructure.Data;

public sealed class TrashRepository : ITrashRepository
{
    private readonly SQLiteAsyncConnection _db;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public TrashRepository(string databasePath)
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
                await _db.CreateTableAsync<TrashMessageEntity>();
                _initialized = true;
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<bool> AddAsync(IReadOnlyCollection<TrashedMessage> messages)
    {
        if (messages.Count == 0)
            return true;

        try
        {
            await EnsureInitializedAsync();
            // One transaction: a conversation of hundreds of messages is kept whole or not at all.
            await _db.InsertAllAsync(messages.Select(m => new TrashMessageEntity
            {
                Address = m.Address,
                Body = m.Body,
                DateMs = m.DateMs,
                Type = m.Type,
                SubId = m.SubId,
                TrashedAtMs = m.TrashedAtMs
            }), runInTransaction: true);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Keeping deleted messages failed: {ex}");
            return false;
        }
    }

    public async Task<IReadOnlyList<TrashedMessage>> GetAllAsync()
    {
        try
        {
            await EnsureInitializedAsync();
            var entities = await _db.Table<TrashMessageEntity>().OrderByDescending(x => x.TrashedAtMs).ToListAsync();
            return entities.Select(e => new TrashedMessage(e.Id, e.Address, e.Body, e.DateMs, e.Type, e.SubId, e.TrashedAtMs)).ToList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Reading the trash failed: {ex}");
            return Array.Empty<TrashedMessage>();
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
                await _db.ExecuteAsync($"DELETE FROM TrashMessage WHERE Id IN ({placeholders})", chunk.Cast<object>().ToArray());
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erasing from the trash failed: {ex}");
        }
    }

    public async Task DeleteAllAsync()
    {
        try
        {
            await EnsureInitializedAsync();
            await _db.DeleteAllAsync<TrashMessageEntity>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Emptying the trash failed: {ex}");
        }
    }

    public async Task DeleteOlderThanAsync(DateTime cutoff)
    {
        try
        {
            await EnsureInitializedAsync();
            await _db.ExecuteAsync("DELETE FROM TrashMessage WHERE TrashedAtMs < ?", new DateTimeOffset(cutoff).ToUnixTimeMilliseconds());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erasing old trash failed: {ex}");
        }
    }
}
