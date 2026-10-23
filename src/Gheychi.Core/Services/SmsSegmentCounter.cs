namespace Gheychi.Core.Services;

/// <summary>How a text is encoded on the air; decides how much of it fits in one part.</summary>
public enum SmsEncoding
{
    /// <summary>The GSM 7-bit default alphabet (with its extension table): 160 characters in one part, 153 in each of several.</summary>
    Gsm7,

    /// <summary>UCS-2, used as soon as one character is outside the GSM alphabet: 70 in one part, 67 in each of several.</summary>
    Ucs2,
}

/// <summary>What a text takes to send: how many parts, and how many characters are left in the last one.</summary>
public readonly record struct SmsSegments(int Parts, int Remaining, SmsEncoding Encoding);

/// <summary>
/// Counts the parts of a message the way <c>SmsManager.DivideMessage</c> splits it, so the number shown while typing is
/// the number sent. The text is counted trimmed, as it is sent.
/// </summary>
public static class SmsSegmentCounter
{
    private const int GsmSingle = 160;
    private const int GsmPart = 153;
    private const int UcsSingle = 70;
    private const int UcsPart = 67;

    // Septets a character takes in GSM 7-bit: 1 in the default table, 2 through the escape (extension table), 0 when it is not there.
    private static readonly byte[] GsmCost = BuildGsmCosts();

    /// <summary>Parts and room left for <paramref name="text"/>; an empty text is one empty part.</summary>
    public static SmsSegments Count(string? text) => Count(text.AsSpan());

    public static SmsSegments Count(ReadOnlySpan<char> text)
    {
        text = text.Trim();

        var septets = 0;
        foreach (var c in text)
        {
            var cost = c < GsmCost.Length ? GsmCost[c] : 0;
            if (cost == 0)
                return CountUcs2(text.Length);
            septets += cost;
        }

        if (septets <= GsmSingle)
            return new SmsSegments(1, GsmSingle - septets, SmsEncoding.Gsm7);

        // An escaped character is never cut in two: one that does not fit at the end of a part starts the next.
        var parts = 1;
        var used = 0;
        foreach (var c in text)
        {
            var cost = GsmCost[c];
            if (used + cost > GsmPart)
            {
                parts++;
                used = 0;
            }

            used += cost;
        }

        return new SmsSegments(parts, GsmPart - used, SmsEncoding.Gsm7);
    }

    private static SmsSegments CountUcs2(int units)
    {
        if (units <= UcsSingle)
            return new SmsSegments(1, UcsSingle - units, SmsEncoding.Ucs2);

        var parts = (units + UcsPart - 1) / UcsPart;
        return new SmsSegments(parts, parts * UcsPart - units, SmsEncoding.Ucs2);
    }

    private static byte[] BuildGsmCosts()
    {
        // The euro sign is the highest character in either table.
        var costs = new byte[0x20AD];

        const string basic =
            "@£$¥èéùìòÇ\nØø\rÅå" +
            "Δ_ΦΓΛΩΠΨΣΘΞ" + "ÆæßÉ" +
            " !\"#¤%&'()*+,-./" +
            "0123456789:;<=>?" +
            "¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§" +
            "¿abcdefghijklmnopqrstuvwxyzäöñüà";
        foreach (var c in basic)
            costs[c] = 1;

        foreach (var c in "\f^{}\\[~]|€")
            costs[c] = 2;

        return costs;
    }
}
