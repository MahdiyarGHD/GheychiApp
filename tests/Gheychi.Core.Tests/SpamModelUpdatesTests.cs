using Gheychi.Core.Spam;
using Xunit;

namespace Gheychi.Core.Tests;

public sealed class SpamModelUpdatesTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    private readonly FakeSource _source = new();
    private readonly FakeUpdater _updater = new() { Active = 2 };
    private readonly FakeState _state = new();

    private SpamModelUpdates Create(DateTime? now = null) => new(_source, _updater, _state, () => now ?? Now);

    private static SpamModelRelease Release(int version) => new(version, 100, $"{{\"version\":{version}}}");

    [Fact]
    public async Task Check_NewerRelease_IsAvailableAndRaisesChanged()
    {
        _source.Latest = Release(3);
        var updates = Create();
        var changed = 0;
        updates.Changed += (_, _) => changed++;

        var found = await updates.CheckAsync(force: false);

        Assert.Equal(Release(3), found);
        Assert.Equal(Release(3), await updates.GetAvailableAsync());
        Assert.Equal(Now, _state.LastCheckedUtc);
        Assert.Equal(1, changed);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(1)]
    public async Task Check_ReleaseNotNewer_IsNotAvailable(int version)
    {
        _source.Latest = Release(version);

        Assert.Null(await Create().CheckAsync(force: false));
        Assert.Null(_state.Available);
    }

    [Fact]
    public async Task Check_WithinInterval_DoesNotAskTheSource()
    {
        _state.LastCheckedUtc = Now.AddHours(-23);
        _source.Latest = Release(3);

        Assert.Null(await Create().CheckAsync(force: false));
        Assert.Equal(0, _source.LatestCalls);
    }

    [Fact]
    public async Task Check_Forced_AsksEvenWithinInterval()
    {
        _state.LastCheckedUtc = Now.AddMinutes(-5);
        _source.Latest = Release(3);

        Assert.Equal(Release(3), await Create().CheckAsync(force: true));
        Assert.Equal(1, _source.LatestCalls);
    }

    [Fact]
    public async Task Check_ClockMovedBack_AsksAgain()
    {
        _state.LastCheckedUtc = Now.AddDays(3);
        _source.Latest = Release(3);

        Assert.Equal(Release(3), await Create().CheckAsync(force: false));
    }

    [Fact]
    public async Task Check_SourceFails_KeepsLastCheckTime()
    {
        _state.LastCheckedUtc = Now.AddDays(-2);
        _source.Fail = true;

        await Assert.ThrowsAsync<HttpRequestException>(() => Create().CheckAsync(force: false));
        Assert.Equal(Now.AddDays(-2), _state.LastCheckedUtc);
    }

    [Fact]
    public async Task Available_AlreadyActive_IsCleared()
    {
        _state.Available = Release(3);
        _updater.Active = 3;

        Assert.Null(await Create().GetAvailableAsync());
        Assert.Null(_state.Available);
    }

    [Fact]
    public async Task Install_Success_ClearsAvailableAndPassesManifest()
    {
        _state.Available = Release(3);
        var updates = Create();

        Assert.True(await updates.InstallAsync(Release(3)));

        Assert.Null(_state.Available);
        Assert.Equal(Release(3).Manifest, _updater.InstalledManifest);
        Assert.True(_source.LastDownload!.Disposed);
    }

    [Fact]
    public async Task Install_Rejected_KeepsAvailable()
    {
        _state.Available = Release(3);
        _updater.Accept = false;

        Assert.False(await Create().InstallAsync(Release(3)));
        Assert.Equal(Release(3), _state.Available);
    }

    private sealed class FakeSource : ISpamModelSource
    {
        public SpamModelRelease? Latest { get; set; }
        public bool Fail { get; set; }
        public int LatestCalls { get; private set; }
        public TrackedStream? LastDownload { get; private set; }

        public Task<SpamModelRelease?> GetLatestAsync(CancellationToken cancellationToken = default)
        {
            LatestCalls++;
            return Fail ? throw new HttpRequestException("offline") : Task.FromResult(Latest);
        }

        public Task<Stream> DownloadAsync(SpamModelRelease release, CancellationToken cancellationToken = default)
        {
            LastDownload = new TrackedStream();
            return Task.FromResult<Stream>(LastDownload);
        }
    }

    private sealed class TrackedStream() : MemoryStream([1, 2, 3])
    {
        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class FakeUpdater : ISpamModelUpdater
    {
        public int? Active { get; set; }
        public bool Accept { get; set; } = true;
        public string? InstalledManifest { get; private set; }

        public Task<int?> GetActiveVersionAsync(CancellationToken cancellationToken = default) => Task.FromResult(Active);

        public Task<bool> InstallAsync(Stream manifest, Stream model, CancellationToken cancellationToken = default)
        {
            if (!Accept)
                return Task.FromResult(false);

            InstalledManifest = new StreamReader(manifest).ReadToEnd();
            Active = System.Text.Json.JsonDocument.Parse(InstalledManifest).RootElement.GetProperty("version").GetInt32();
            return Task.FromResult(true);
        }
    }

    private sealed class FakeState : ISpamModelUpdateState
    {
        public DateTime? LastCheckedUtc { get; set; }
        public SpamModelRelease? Available { get; set; }
    }
}
