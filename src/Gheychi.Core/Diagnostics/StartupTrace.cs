using System.Globalization;
using System.Text;

namespace Gheychi.Core.Diagnostics;

/// <summary>
/// Named moments of one launch, in milliseconds since the process started. A cold start can only be timed on the
/// phone itself, so this runs in the release build too; marks may come from any thread.
/// </summary>
public sealed class StartupTrace(long processStartMs, Func<long> nowMs)
{
    private readonly List<(string Name, long AtMs)> _marks = [];

    public void Mark(string name)
    {
        var at = nowMs() - processStartMs;
        lock (_marks)
            _marks.Add((name, at));
    }

    public long? ElapsedAt(string name)
    {
        lock (_marks)
        {
            foreach (var (each, at) in _marks)
            {
                if (each == name)
                    return at;
            }
        }

        return null;
    }

    public string Format(DateTime startedAt)
    {
        List<(string Name, long AtMs)> marks;
        lock (_marks)
            marks = [.. _marks.OrderBy(m => m.AtMs)];

        // Invariant: a Persian phone would otherwise write a Persian-calendar date and Persian digits, and reports from
        // different runs and phones are compared side by side.
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"run {startedAt:yyyy-MM-dd HH:mm:ss}\n");
        var previous = 0L;
        foreach (var (name, at) in marks)
        {
            text.Append(CultureInfo.InvariantCulture, $"{at,6} ms  +{at - previous,5}  {name}\n");
            previous = at;
        }

        return text.ToString();
    }
}

public static class StartupHistory
{
    public const int MaxRuns = 10;
    private const string Separator = "---\n";

    /// <summary>Newest run first; runs beyond <see cref="MaxRuns"/> are dropped.</summary>
    public static string Prepend(string history, string run) =>
        string.Join(Separator, history.Split(Separator, StringSplitOptions.RemoveEmptyEntries).Prepend(run).Take(MaxRuns));
}
