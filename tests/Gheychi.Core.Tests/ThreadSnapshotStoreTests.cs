using Gheychi.Core.Models;
using Gheychi.Core.Services;
using Xunit;

namespace Gheychi.Core.Tests;

public sealed class ThreadSnapshotStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gheychi-snap-" + Guid.NewGuid().ToString("N"));

    public ThreadSnapshotStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        var path = Path.Combine(_dir, "threads.json");
        var thread = new SmsThread(5, "+989121110000", "Ali", "hi “there”", new DateTime(2026, 10, 4, 9, 30, 0), 12, 2, true, 3);

        ThreadSnapshotStore.Save(path, [thread]);
        var loaded = ThreadSnapshotStore.TryLoad(path);

        Assert.Equal([thread], loaded);
    }

    [Fact]
    public void TryLoad_MissingFile_ReturnsEmpty()
    {
        Assert.Empty(ThreadSnapshotStore.TryLoad(Path.Combine(_dir, "nope.json")));
    }

    [Fact]
    public void TryLoad_CorruptFile_ReturnsEmptyInsteadOfThrowing()
    {
        var path = Path.Combine(_dir, "bad.json");
        File.WriteAllText(path, "{ not json");
        Assert.Empty(ThreadSnapshotStore.TryLoad(path));
    }

    [Fact]
    public void Save_KeepsOnlyTheNewestThreads()
    {
        var path = Path.Combine(_dir, "many.json");
        var threads = Enumerable.Range(1, 500)
            .Select(i => new SmsThread(i, "+1", null, "s", DateTime.UnixEpoch, 1, 0, false, 1))
            .ToList();

        ThreadSnapshotStore.Save(path, threads);

        var loaded = ThreadSnapshotStore.TryLoad(path);
        Assert.Equal(300, loaded.Count);
        Assert.Equal(1, loaded[0].ThreadId);
    }
}
