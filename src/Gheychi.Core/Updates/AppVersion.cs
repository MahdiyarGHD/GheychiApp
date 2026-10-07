using System.Globalization;
using System.Text.RegularExpressions;

namespace Gheychi.Core.Updates;

/// <summary>
/// A release version as the release workflow tags it: 1.2.3 for stable, 1.2.3-rc or 1.2.3-rc.1 for a release
/// candidate, which comes before the stable release of the same number.
/// </summary>
public readonly record struct AppVersion(int Major, int Minor, int Patch, int? Candidate) : IComparable<AppVersion>
{
    // Same shape release.yml accepts for a tag; the patch is optional so that "1.0", a local build's version, still reads.
    private static readonly Regex Pattern = new(@"^v?(\d+)\.(\d+)(?:\.(\d+))?(?:-rc(?:[.-]?(\d+))?)?$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public bool IsCandidate => Candidate is not null;

    public static AppVersion? Parse(string? text)
    {
        var match = Pattern.Match(text?.Trim() ?? string.Empty);
        if (!match.Success)
            return null;

        static int Read(Group group) => group.Success ? int.Parse(group.Value, CultureInfo.InvariantCulture) : 0;

        var isCandidate = match.Value.Contains("-rc", StringComparison.OrdinalIgnoreCase);
        return new AppVersion(Read(match.Groups[1]), Read(match.Groups[2]), Read(match.Groups[3]),
            isCandidate ? Read(match.Groups[4]) : null);
    }

    public int CompareTo(AppVersion other)
    {
        var result = (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
        if (result != 0)
            return result;

        // A stable release is newer than any of its candidates.
        return (Candidate, other.Candidate) switch
        {
            (null, null) => 0,
            (null, _) => 1,
            (_, null) => -1,
            ({ } a, { } b) => a.CompareTo(b)
        };
    }

    public static bool operator >(AppVersion left, AppVersion right) => left.CompareTo(right) > 0;

    public static bool operator <(AppVersion left, AppVersion right) => left.CompareTo(right) < 0;
}
