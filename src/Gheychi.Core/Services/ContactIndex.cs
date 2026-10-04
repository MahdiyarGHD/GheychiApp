using Gheychi.Core.Models;

namespace Gheychi.Core.Services;

/// <summary>
/// Address-book entries sorted for a contact picker, with what searching needs worked out once.
/// Building it is the expensive part (sorting, digit extraction); <see cref="Match"/> is a cheap scan.
/// </summary>
public sealed class ContactIndex
{
    private const int NumberOnlyRank = 4;

    private readonly ContactEntry[] _entries;
    private readonly string[] _letters;
    private readonly string[] _digits;

    public ContactIndex(IEnumerable<ContactEntry> contacts)
    {
        var sorted = contacts
            .Select(c => (Entry: c, Letter: ContactListBuilder.SectionLetter(c.Name)))
            .OrderBy(x => x.Letter == ContactListBuilder.OtherLetter ? 1 : 0)
            .ThenBy(x => x.Letter, StringComparer.Ordinal)
            .ThenBy(x => x.Entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Entry.Number, StringComparer.Ordinal)
            .ToArray();

        _entries = sorted.Select(x => x.Entry).ToArray();
        _letters = sorted.Select(x => x.Letter).ToArray();
        _digits = _entries.Select(e => DigitsOf(e.Number)).ToArray();

        var hash = new HashCode();
        foreach (var entry in _entries)
        {
            hash.Add(entry.Name);
            hash.Add(entry.Number);
        }
        Signature = hash.ToHashCode();
    }

    public int Count => _entries.Length;

    /// <summary>Changes when a contact is added, removed or edited; lets a reload skip an identical list.</summary>
    public int Signature { get; }

    public ContactEntry this[int index] => _entries[index];

    /// <summary>Section letter of the entry at <paramref name="index"/> ("#" for names not starting with a letter).</summary>
    public string LetterAt(int index) => _letters[index];

    /// <summary>
    /// Indices of the entries matching <paramref name="query"/> (name, or digits of the number).
    /// A blank query returns everything in sorted order; otherwise the best name matches come first
    /// (equal, starts with, word starts with, contains), then number-only matches.
    /// </summary>
    public IReadOnlyList<int> Match(string? query)
    {
        var needles = SearchTextHelper.BuildVariants(query);
        if (needles.Count == 0)
            return Enumerable.Range(0, _entries.Length).ToArray();

        var queryDigits = DigitsOf(query ?? string.Empty);
        var hits = new List<(int Index, int Rank)>();

        for (var i = 0; i < _entries.Length; i++)
        {
            var rank = SearchTextHelper.TitleRank(_entries[i].Name, needles);
            if (rank == SearchTextHelper.NoTitleMatch)
            {
                var numberMatches = SearchTextHelper.ContainsAny(_entries[i].Number, needles) ||
                                    (queryDigits.Length > 0 && _digits[i].Contains(queryDigits, StringComparison.Ordinal));
                if (!numberMatches)
                    continue;

                rank = NumberOnlyRank;
            }

            hits.Add((i, rank));
        }

        // List.Sort is not stable, so ties are broken by the (already alphabetical) index.
        hits.Sort(static (a, b) => a.Rank != b.Rank ? a.Rank.CompareTo(b.Rank) : a.Index.CompareTo(b.Index));
        return hits.Select(h => h.Index).ToArray();
    }

    private static string DigitsOf(string text) =>
        new(SearchTextHelper.ToAsciiDigits(text).Where(c => c is >= '0' and <= '9').ToArray());
}
