using Gheychi.Core.Spam;
using Gheychi.Infrastructure.Data;
using Xunit;

namespace Gheychi.Infrastructure.Tests;

public sealed class SpamStatRepositoryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"gheychi_stats_test_{Guid.NewGuid():N}.db");

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

    private static readonly DateTime At = new(2026, 10, 1, 9, 30, 0, DateTimeKind.Local);

    [Fact]
    public async Task AddMissing_SkipsLoggedMessages()
    {
        var repository = new SpamStatRepository(_dbPath);
        await repository.AddAsync(new SpamStat(0, 1, "Bank", At, 0.9f, 1));

        await repository.AddMissingAsync([new SpamStat(0, 1, "Bank", At, 0.9f, 1), new SpamStat(0, 2, "9121234567", At.AddHours(1), 0.97f, 1)]);

        var all = await repository.GetAllAsync();
        Assert.Equal([1L, 2L], all.Select(s => s.SpamMessageId).Order());
        Assert.Equal(new SpamStat(all[0].Id, 1, "Bank", At, 0.9f, 1), all.Single(s => s.SpamMessageId == 1));
    }

    [Fact]
    public async Task MarkRestored_FlagsOnlyThatMessage()
    {
        var repository = new SpamStatRepository(_dbPath);
        await repository.AddAsync(new SpamStat(0, 1, "Bank", At, 0.9f, 1));
        await repository.AddAsync(new SpamStat(0, 2, "Shop", At, 0.9f, 1));

        await repository.MarkRestoredAsync(2);

        var all = await repository.GetAllAsync();
        Assert.False(all.Single(s => s.SpamMessageId == 1).Restored);
        Assert.True(all.Single(s => s.SpamMessageId == 2).Restored);
    }

    [Fact]
    public async Task Stats_OutliveClearedSpam()
    {
        var spam = new SpamMessageRepository(_dbPath);
        var stats = new SpamStatRepository(_dbPath);
        var id = await spam.AddAsync(new SpamMessage(0, "Bank", "win", At, 0, 0.9f, 1, 0.85f));
        await stats.AddAsync(new SpamStat(0, id, "Bank", At, 0.9f, 1));

        await spam.DeleteAllAsync();

        Assert.Empty(await spam.GetAllAsync());
        Assert.Single(await stats.GetAllAsync());
    }
}
