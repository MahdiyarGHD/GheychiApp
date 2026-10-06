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
}
