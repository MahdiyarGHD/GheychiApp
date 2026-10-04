using Gheychi.Infrastructure.Data;
using Xunit;

namespace Gheychi.Infrastructure.Tests;

public sealed class MessageMetadataRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly MessageMetadataRepository _repository;

    public MessageMetadataRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"gheychi_test_{Guid.NewGuid():N}.db");
        _repository = new MessageMetadataRepository(_dbPath);
    }

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
    public async Task SetStarredAsync_PersistsAndRetrievesCorrectly()
    {
        await _repository.SetStarredAsync(101, 1, true);

        var item = await _repository.GetAsync(101);
        Assert.NotNull(item);
        Assert.True(item.IsStarred);
        Assert.Equal(1, item.ThreadId);

        await _repository.SetStarredAsync(101, 1, false);
        var updated = await _repository.GetAsync(101);
        Assert.NotNull(updated);
        Assert.False(updated.IsStarred);
    }

    [Fact]
    public async Task SetReactionAsync_PersistsAndUpdatesCorrectly()
    {
        await _repository.SetReactionAsync(202, 2, "👍", true);

        var item = await _repository.GetAsync(202);
        Assert.NotNull(item);
        Assert.Equal("👍", item.Reaction);
        Assert.True(item.ReactionFromMe);

        await _repository.SetReactionAsync(202, 2, "✂️", true);
        var updated = await _repository.GetAsync(202);
        Assert.NotNull(updated);
        Assert.Equal("✂️", updated.Reaction);
    }

    [Fact]
    public async Task GetForMessagesAsync_ReturnsMatchedItems()
    {
        await _repository.SetStarredAsync(301, 3, true);
        await _repository.SetReactionAsync(302, 3, "❤️", false);

        var dict = await _repository.GetForMessagesAsync([301, 302, 303]);

        Assert.Equal(2, dict.Count);
        Assert.True(dict[301].IsStarred);
        Assert.Equal("❤️", dict[302].Reaction);
        Assert.False(dict.ContainsKey(303));
    }

    [Fact]
    public async Task DeleteAsync_RemovesMetadata()
    {
        await _repository.SetStarredAsync(401, 4, true);
        await _repository.DeleteAsync([401]);

        var item = await _repository.GetAsync(401);
        Assert.Null(item);
    }

    [Fact]
    public async Task DeleteForThreadsAsync_RemovesMetadataForThread()
    {
        await _repository.SetStarredAsync(501, 5, true);
        await _repository.SetStarredAsync(502, 5, true);
        await _repository.SetStarredAsync(601, 6, true);

        await _repository.DeleteForThreadsAsync([5]);

        var item501 = await _repository.GetAsync(501);
        var item502 = await _repository.GetAsync(502);
        var item601 = await _repository.GetAsync(601);

        Assert.Null(item501);
        Assert.Null(item502);
        Assert.NotNull(item601);
    }
}
