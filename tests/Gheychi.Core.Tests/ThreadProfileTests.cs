using Gheychi.Core.Notifications;
using Gheychi.Core.Services;
using Xunit;

namespace Gheychi.Core.Tests;

public class ThreadProfileTests
{
    private sealed class FakeSettings : IThreadSettings
    {
        public long Until { get; set; }

        public long GetSnoozedUntil(long threadId) => Until;
        public void SetSnoozedUntil(long threadId, long untilMillis) => Until = untilMillis;
        public int GetPreferredSubId(long threadId) => 0;
        public void SetPreferredSubId(long threadId, int subId) { }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 5, 15, 40, 0, TimeSpan.FromHours(3.5));

    [Theory]
    [InlineData("+15550192834", true, true, true, true)]
    [InlineData("09120000001", true, true, true, true)]
    [InlineData("Bank Mellat", false, true, true, true)]
    [InlineData("3000", false, true, true, true)]
    public void Each_button_has_its_own_rule(string address, bool call, bool text, bool details, bool search)
    {
        var actions = ThreadProfileActions.For(address);

        Assert.Equal((call, text, details, search), (actions.Call, actions.Text, actions.Details, actions.Search));
    }

    [Fact]
    public void Visible_count_counts_only_shown_buttons()
    {
        Assert.Equal(4, ThreadProfileActions.For("+15550192834").VisibleCount);
        Assert.Equal(3, ThreadProfileActions.For("Bank Mellat").VisibleCount);
    }

    [Fact]
    public void Snooze_presets_end_at_the_expected_moment()
    {
        Assert.Equal(Now.AddHours(1).ToUnixTimeMilliseconds(), SnoozeSchedule.UntilMillis(SnoozePreset.OneHour, Now));
        Assert.Equal(Now.AddHours(8).ToUnixTimeMilliseconds(), SnoozeSchedule.UntilMillis(SnoozePreset.EightHours, Now));
        Assert.Equal(Now.AddDays(7).ToUnixTimeMilliseconds(), SnoozeSchedule.UntilMillis(SnoozePreset.OneWeek, Now));
        Assert.Equal(SnoozeSchedule.Indefinite, SnoozeSchedule.UntilMillis(SnoozePreset.Forever, Now));
    }

    [Fact]
    public void Tomorrow_morning_is_eight_local_time_the_next_day()
    {
        var until = DateTimeOffset.FromUnixTimeMilliseconds(SnoozeSchedule.UntilMillis(SnoozePreset.TomorrowMorning, Now));

        Assert.Equal(new DateTimeOffset(2026, 10, 6, 8, 0, 0, Now.Offset), until);
    }

    [Fact]
    public void Snoozed_thread_is_suppressed_until_the_snooze_ends()
    {
        var settings = new FakeSettings { Until = Now.AddHours(1).ToUnixTimeMilliseconds() };
        var message = new IncomingMessage(7, "+15550100", "hi", 1_000, 1);

        Assert.Equal(NotificationDecision.Suppress, new SnoozeRule(settings, new FixedTime(Now)).Evaluate(message));
        Assert.Equal(NotificationDecision.Show, new SnoozeRule(settings, new FixedTime(Now.AddHours(2))).Evaluate(message));
    }

    [Fact]
    public void Thread_that_was_never_snoozed_is_shown()
    {
        var message = new IncomingMessage(7, "+15550100", "hi", 1_000, 1);

        Assert.Equal(NotificationDecision.Show, new SnoozeRule(new FakeSettings(), new FixedTime(Now)).Evaluate(message));
    }

    [Fact]
    public void Indefinite_snooze_never_ends()
    {
        var settings = new FakeSettings { Until = SnoozeSchedule.Indefinite };
        var message = new IncomingMessage(7, "+15550100", "hi", 1_000, 1);

        Assert.Equal(NotificationDecision.Suppress, new SnoozeRule(settings, new FixedTime(Now.AddYears(5))).Evaluate(message));
    }
}
