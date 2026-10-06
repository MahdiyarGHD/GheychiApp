using Gheychi.Core.Spam;
using Gheychi.Infrastructure.Data;
using Xunit;

namespace Gheychi.Infrastructure.Tests;

public sealed class SpamMessageRepositoryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"gheychi_spam_test_{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        try
        {
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }
        catch
        {
        }
    }

    [Fact]
    public async Task GetAllAsync_ReturnsStoredMessagesNewestFirst()
    {
        var repository = new SpamMessageRepository(_dbPath);
        var older = new DateTime(2026, 10, 1, 9, 30, 0, DateTimeKind.Local);
        var newer = older.AddHours(2);

        var olderId = await repository.AddAsync(new SpamMessage(0, "+989121234567", "older", older, 2, 0.9f, 1));
        var newerId = await repository.AddAsync(new SpamMessage(0, "Bank", "newer", newer, 0, 0.99f, 1));

        var all = await repository.GetAllAsync();

        Assert.True(olderId > 0);
        Assert.True(newerId > olderId);
        Assert.Equal(["newer", "older"], all.Select(m => m.Body));
        Assert.Equal(new SpamMessage(olderId, "+989121234567", "older", older, 2, 0.9f, 1), all[1]);
    }

    [Fact]
    public async Task DeleteMethods_RemoveTheRightRows()
    {
        var repository = new SpamMessageRepository(_dbPath);
        var now = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Local);

        var oldId = await repository.AddAsync(new SpamMessage(0, "a", "old", now.AddDays(-31), 0, 0.9f, 1));
        var keepId = await repository.AddAsync(new SpamMessage(0, "b", "keep", now.AddDays(-1), 0, 0.9f, 1));
        var deleteId = await repository.AddAsync(new SpamMessage(0, "c", "delete", now, 0, 0.9f, 1));

        await repository.DeleteOlderThanAsync(now.AddDays(-30));
        await repository.DeleteAsync([deleteId]);

        Assert.Equal([keepId], (await repository.GetAllAsync()).Select(m => m.Id));
        Assert.NotEqual(0, oldId);

        await repository.DeleteAllAsync();
        Assert.Empty(await repository.GetAllAsync());
    }
}
