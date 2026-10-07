using Gheychi.Core.Updates;
using Xunit;

namespace Gheychi.Core.Tests;

public sealed class AppUpdatesTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    private readonly FakeSource _source = new();
    private readonly FakeState _state = new();

    private AppUpdates Create(string installed, DateTime? now = null) => new(_source, _state, installed, () => now ?? Now);

    private static AppRelease Release(string tag) => new(tag, $"https://example.test/releases/tag/{tag}");

    [Fact]
    public async Task Check_Stable_OffersTheNewestStableAndRaisesChanged()
    {
        _source.Releases = [Release("1.2.0"), Release("1.4.0"), Release("1.3.0"), Release("1.5.0-rc.1")];
        var updates = Create("1.2.0");
        var changed = 0;
        updates.Changed += (_, _) => changed++;

        var found = await updates.CheckAsync(force: false);

        Assert.Equal(Release("1.4.0"), found);
        Assert.Equal(Release("1.4.0"), updates.GetAvailable());
        Assert.Equal(Now, _state.LastCheckedUtc);
        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task Check_NewestHasNoApkYet_OffersTheNewestThatHasOne()
    {
        _source.Releases = [Release("1.2.0"), Release("1.3.0"), Release("1.4.0")];
        _source.WithoutApk = ["1.4.0"];

        Assert.Equal(Release("1.3.0"), await Create("1.2.0").CheckAsync(force: false));
    }

    [Fact]
    public async Task Check_OnlyNewerHasNoApkYet_IsNotAvailable()
    {
        _source.Releases = [Release("1.2.0"), Release("1.3.0")];
        _source.WithoutApk = ["1.3.0"];

        Assert.Null(await Create("1.2.0").CheckAsync(force: false));
    }

    [Fact]
    public async Task Check_Stable_IsNotOfferedCandidates()
    {
        _source.Releases = [Release("1.2.0"), Release("1.3.0-rc.1")];

        Assert.Null(await Create("1.2.0").CheckAsync(force: false));
    }

    [Fact]
    public async Task Check_Candidate_IsOfferedNewerCandidates()
    {
        _source.Releases = [Release("1.3.0-rc.1"), Release("1.3.0-rc.2"), Release("1.2.0")];

        Assert.Equal(Release("1.3.0-rc.2"), await Create("1.3.0-rc.1").CheckAsync(force: false));
    }

    [Fact]
    public async Task Check_Candidate_IsOfferedItsStableRelease()
    {
        _source.Releases = [Release("1.3.0-rc.2"), Release("1.3.0")];

        Assert.Equal(Release("1.3.0"), await Create("1.3.0-rc.2").CheckAsync(force: false));
    }

    [Fact]
    public async Task Check_NothingNewer_IsNotAvailable()
    {
        _source.Releases = [Release("1.2.0"), Release("1.1.0")];

        Assert.Null(await Create("1.2.0").CheckAsync(force: false));
    }

    [Fact]
    public async Task Check_UnreadableInstalledVersion_OffersNothing()
    {
        _source.Releases = [Release("9.0.0")];

        Assert.Null(await Create("dev").CheckAsync(force: false));
    }

    [Fact]
    public async Task Check_WithinInterval_DoesNotAskTheSource()
    {
        _state.LastCheckedUtc = Now.AddHours(-2);

        await Create("1.0.0").CheckAsync(force: false);

        Assert.Equal(0, _source.Calls);
    }

    [Fact]
    public async Task Check_Forced_AsksEvenWithinInterval()
    {
        _state.LastCheckedUtc = Now.AddHours(-2);

        await Create("1.0.0").CheckAsync(force: true);

        Assert.Equal(1, _source.Calls);
    }

    [Fact]
    public async Task Check_SourceFails_KeepsLastCheckTime()
    {
        _state.LastCheckedUtc = Now.AddDays(-3);
        _source.Fail = true;

        await Assert.ThrowsAsync<HttpRequestException>(() => Create("1.0.0").CheckAsync(force: false));
        Assert.Equal(Now.AddDays(-3), _state.LastCheckedUtc);
    }

    [Fact]
    public void Available_AfterTheAppWasUpdated_IsCleared()
    {
        _state.Available = Release("1.3.0");

        Assert.Null(Create("1.3.0").GetAvailable());
        Assert.Null(_state.Available);
    }

    private sealed class FakeSource : IAppReleaseSource
    {
        public IReadOnlyList<AppRelease> Releases { get; set; } = [];
        public bool Fail { get; set; }
        public int Calls { get; private set; }
        public IReadOnlyList<string> WithoutApk { get; set; } = [];

        public Task<IReadOnlyList<AppRelease>> GetReleasesAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Fail ? throw new HttpRequestException("offline") : Task.FromResult(Releases);
        }

        public Task<bool> HasApkAsync(AppRelease release, CancellationToken cancellationToken = default) =>
            Task.FromResult(!WithoutApk.Contains(release.Tag));
    }

    private sealed class FakeState : IAppUpdateState
    {
        public DateTime? LastCheckedUtc { get; set; }
        public AppRelease? Available { get; set; }
    }
}
