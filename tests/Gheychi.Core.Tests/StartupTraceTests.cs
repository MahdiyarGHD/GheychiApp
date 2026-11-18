using Gheychi.Core.Diagnostics;
using Xunit;

namespace Gheychi.Core.Tests;

public class StartupTraceTests
{
    [Fact]
    public void Marks_are_measured_from_process_start_and_listed_in_time_order()
    {
        var now = 0L;
        var trace = new StartupTrace(processStartMs: 400, () => now);
        now = 700; trace.Mark("b");
        now = 650; trace.Mark("a");
        now = 1_300; trace.Mark("c");

        var lines = trace.Format(new DateTime(2026, 11, 18, 9, 30, 0)).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("run 2026-11-18 09:30:00", lines[0]);
        Assert.Equal("   250 ms  +  250  a", lines[1]);
        Assert.Equal("   300 ms  +   50  b", lines[2]);
        Assert.Equal("   900 ms  +  600  c", lines[3]);
    }

    [Fact]
    public void ElapsedAt_returns_the_first_mark_of_that_name_or_null()
    {
        var now = 0L;
        var trace = new StartupTrace(100, () => now);
        now = 350; trace.Mark("activity.oncreate");
        now = 900; trace.Mark("activity.oncreate");

        Assert.Equal(250, trace.ElapsedAt("activity.oncreate"));
        Assert.Null(trace.ElapsedAt("gate.open"));
    }

    [Fact]
    public void History_keeps_the_newest_runs_first_and_drops_the_oldest()
    {
        var history = "";
        for (var i = 1; i <= StartupHistory.MaxRuns + 2; i++)
            history = StartupHistory.Prepend(history, $"run {i}\n");

        var runs = history.Split("---\n");
        Assert.Equal(StartupHistory.MaxRuns, runs.Length);
        Assert.Equal($"run {StartupHistory.MaxRuns + 2}\n", runs[0]);
        Assert.Equal("run 3\n", runs[^1]);
    }
}
