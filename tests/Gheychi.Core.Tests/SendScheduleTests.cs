using Gheychi.Core.Scheduling;
using Xunit;

namespace Gheychi.Core.Tests;

public class SendScheduleTests
{
    private static readonly TimeSpan Offset = TimeSpan.FromHours(3.5);

    private static DateTimeOffset At(int hour, int minute = 0) => new(2026, 10, 17, hour, minute, 0, Offset);

    [Fact]
    public void Tomorrow_morning_is_eight_the_next_day_in_the_users_offset()
    {
        var expected = new DateTimeOffset(2026, 10, 18, 8, 0, 0, Offset).ToUnixTimeMilliseconds();

        Assert.Equal(expected, SendSchedule.AtMillis(SchedulePreset.TomorrowMorning, At(21)));
    }

    [Fact]
    public void This_evening_is_six_today()
    {
        var expected = new DateTimeOffset(2026, 10, 17, 18, 0, 0, Offset).ToUnixTimeMilliseconds();

        Assert.Equal(expected, SendSchedule.AtMillis(SchedulePreset.ThisEvening, At(9)));
    }

    [Fact]
    public void This_evening_is_not_offered_once_it_is_nearly_evening()
    {
        Assert.Contains(SchedulePreset.ThisEvening, SendSchedule.Presets(At(16)));
        Assert.DoesNotContain(SchedulePreset.ThisEvening, SendSchedule.Presets(At(17)));
        Assert.DoesNotContain(SchedulePreset.ThisEvening, SendSchedule.Presets(At(20)));
    }

    [Fact]
    public void One_hour_is_an_hour_from_now()
    {
        Assert.Equal(At(10, 30).AddHours(1).ToUnixTimeMilliseconds(), SendSchedule.AtMillis(SchedulePreset.OneHour, At(10, 30)));
    }

    [Fact]
    public void A_picked_day_and_time_keeps_the_users_offset()
    {
        var expected = new DateTimeOffset(2026, 11, 2, 7, 45, 0, Offset).ToUnixTimeMilliseconds();

        Assert.Equal(expected, SendSchedule.AtMillis(new DateTime(2026, 11, 2, 23, 59, 0), 7, 45, At(10)));
    }

    [Fact]
    public void A_time_less_than_a_minute_ahead_is_not_acceptable()
    {
        var now = At(10);

        Assert.False(SendSchedule.IsAcceptable(now.AddSeconds(30).ToUnixTimeMilliseconds(), now));
        Assert.False(SendSchedule.IsAcceptable(now.AddMinutes(-5).ToUnixTimeMilliseconds(), now));
        Assert.True(SendSchedule.IsAcceptable(now.AddMinutes(2).ToUnixTimeMilliseconds(), now));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 5)]
    [InlineData(30, 30)]
    [InlineData(7, 7)]
    [InlineData(8, 0)]
    [InlineData(-3, 0)]
    public void Only_the_offered_delays_are_kept(int stored, int expected)
    {
        Assert.Equal(expected, SendSchedule.NormalizeDelay(stored));
    }
}
