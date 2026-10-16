using System.Text.RegularExpressions;

namespace Gheychi.Core.Services;

/// <summary>A one-time code found in a message: where it is as written, and the code itself as Latin digits (or letters) only.</summary>
/// <param name="Score">How sure the detector is; only codes that passed the bar are returned.</param>
/// <param name="IsConfident">
/// Sure enough to be filled in without asking: the message names it plainly, and nothing else in it competes for being the code.
/// </param>
public readonly record struct OtpCode(int Start, int Length, string Code, double Score, bool IsConfident)
{
    public int End => Start + Length;
}

/// <summary>
/// Picks the one-time code out of a message ("Your code is 482913", "کد تایید: ۴۸۲۱۹"). A number is a code only when
/// the message calls it one: a word like "code", "OTP" or "رمز پویا" stands next to it, and it is not an amount, a
/// date, a phone number, a tracking or order number. Each candidate is scored on how it is named, how far the name
/// is, and what the rest of the message says ("do not share", "valid for 2 minutes"). Meant for highlighting now,
/// and for filling the code in later: <see cref="OtpCode.IsConfident"/> is the bar for doing that unasked.
/// </summary>
public static partial class OtpDetector
{
    private const double AcceptScore = 9;
    private const double ConfidentScore = 11;
    private const double ConfidentMargin = 3;
    private const double StrongLabel = 13;
    private const double WeakLabel = 10;
    private const double LabelAfterPenalty = 2;
    private const double PromoPenalty = 5;
    private const int MaxLabelWords = 5;
    private const int MaxMessageLength = 1000;

    // The browser/Android "origin-bound" format: the last line is "@example.com #123456".
    [GeneratedRegex(@"(?m)^@[\w.\-]+(?::\d+)?[ \t]+#(?<code>[A-Za-z0-9]{4,12})[ \t]*\r?$", RegexOptions.CultureInvariant)]
    private static partial Regex OriginBoundRegex();

    // A whole run of digits on its own: not a part of a longer number, an amount, a time or a word ("A1234", "4G").
    [GeneratedRegex(@"(?<![\w.,٬٫/:+*#])(?<!\d-)\d{4,8}(?!\w|[.,٬٫/:\-]\d)", RegexOptions.CultureInvariant)]
    private static partial Regex DigitsRegex();

    // "123 456", "1234-5678": a code written in two groups.
    [GeneratedRegex(@"(?<![\w.,٬٫/:+*#\-])(?<!\d[ \-])\d{3,4}[ \-]\d{3,4}(?!\w|[ \-.,٬٫/:]\d)", RegexOptions.CultureInvariant)]
    private static partial Regex GroupedRegex();

    // "X7K2P9": capitals and digits mixed.
    [GeneratedRegex(@"(?<![\w\-])(?=[A-Z0-9]{5,8}(?!\w))(?=[A-Z0-9]*\d)(?=[A-Z0-9]*[A-Z])[A-Z0-9]{5,8}", RegexOptions.CultureInvariant)]
    private static partial Regex MixedRegex();

    [GeneratedRegex(
        @"(?:do not|don't|dont|never|not)\s+(?:share|disclose|tell|give|reveal|forward)|با کسی|به کسی|کسی در میان|در اختیار|افشا|اعلام نکنید|ارائه ندهید|نگویید|به اشتراک",
        RegexOptions.CultureInvariant)]
    private static partial Regex ShareWarningRegex();

    [GeneratedRegex(
        @"valid (?:for|until|only)|expires?\b|expiry|within \d+|\d+\s*(?:min|minute|minutes|sec|second|seconds|hour|hours)\b|معتبر|اعتبار|منقضی|دقیقه|ثانیه",
        RegexOptions.CultureInvariant)]
    private static partial Regex ValidityRegex();

    [GeneratedRegex(@"\b(?:enter|use|type|input|submit)\b|وارد|استفاده", RegexOptions.CultureInvariant)]
    private static partial Regex EnterRegex();

    // Words that name a code in a way that makes it a one-time code.
    private static readonly HashSet<string> Strong = new(StringComparer.Ordinal)
    {
        "otp", "verification", "verify", "verifying", "authentication", "authenticate", "authorization", "authorisation",
        "passcode", "2fa", "onetime", "one-time", "security", "login", "log-in", "signin", "sign-in", "confirmation",
        "confirm", "validation", "activation", "doğrulama", "verificación", "vérification", "подтверждения", "проверочный",
        "تایید", "تاییدیه", "تأیید", "احراز", "پویا", "فعال", "یکبار", "یکبارمصرف", "امنیتی", "فعالسازی", "التحقق", "تحقق"
    };

    // Words that name a code that may or may not be a one-time one.
    private static readonly HashSet<string> Weak = new(StringComparer.Ordinal)
    {
        "code", "codes", "pin", "password", "passkey", "token", "kod", "kodu", "код", "código", "codigo", "codice",
        "کد", "کود", "رمز", "پسورد", "گذرواژه", "ورود"
    };

    // What a number next to it makes it something else than a code: an order, an amount, a card, a promotion.
    private static readonly HashSet<string> Qualifier = new(StringComparer.Ordinal)
    {
        "tracking", "track", "promo", "promotional", "discount", "coupon", "voucher", "referral", "invite", "invitation",
        "zip", "postal", "post", "area", "country", "dial", "order", "invoice", "receipt", "reference", "ref", "booking",
        "reservation", "ticket", "flight", "account", "acct", "iban", "card", "amount", "balance", "total", "price", "bill",
        "id", "policy", "case", "پیگیری", "رهگیری", "سفارش", "تخفیف", "کوپن", "معرف", "دعوت", "پستی", "ملی", "مبلغ",
        "موجودی", "حساب", "کارت", "قبض", "شناسه", "مرسوله", "بلیط", "پرواز", "فاکتور", "رزرو", "شماره", "شبا"
    };

    // A number followed by one of these is an amount, a size or a duration.
    private static readonly HashSet<string> Unit = new(StringComparer.Ordinal)
    {
        "ریال", "تومان", "تومن", "ریالی", "تومانی", "rial", "rials", "toman", "tomans", "irr", "usd", "eur", "gbp", "dollar",
        "dollars", "euro", "euros", "درصد", "percent", "gb", "mb", "kb", "km", "kg", "گیگ", "مگ", "میلیون", "میلیارد", "هزار",
        "million", "billion", "thousand", "min", "mins", "minute", "minutes", "sec", "secs", "second", "seconds", "hour",
        "hours", "دقیقه", "ثانیه", "ساعت", "روز", "days", "day"
    };

    private const string Punctuation = ":#-=()[]{}\"'«»<>.,;!?؟،؛*_/\\|~٬";

    /// <summary>The most likely code of the message, or null when it has none.</summary>
    /// <param name="excluded">Tells whether a range of the text is taken by something else already (a link, a phone number).</param>
    public static OtpCode? Find(string? text, Func<int, int, bool>? excluded = null)
    {
        OtpCode? best = null;
        foreach (var code in Analyze(text, excluded))
        {
            if (best is null || code.Score > best.Value.Score)
                best = code;
        }

        return best;
    }

    /// <summary>Every number of the message that passes for a code, in order of appearance.</summary>
    public static IReadOnlyList<OtpCode> FindAll(string? text, Func<int, int, bool>? excluded = null) =>
        Analyze(text, excluded);

    private static List<OtpCode> Analyze(string? text, Func<int, int, bool>? excluded)
    {
        var found = new List<OtpCode>();
        if (string.IsNullOrEmpty(text) || text.Length > MaxMessageLength || !HasDigitRun(text))
            return found;

        var origin = OriginBoundRegex().Matches(text);
        if (origin.Count > 0)
        {
            var group = origin[^1].Groups["code"];
            found.Add(new OtpCode(group.Index, group.Length, group.Value, 100, true));
            return found;
        }

        var normal = Normalize(text);
        var masked = DateMask.Apply(normal);
        var context = ContextBonus(normal);
        var warned = ShareWarningRegex().IsMatch(normal) || ValidityRegex().IsMatch(normal);
        var taken = new List<(int Start, int End)>();

        foreach (Match match in GroupedRegex().Matches(masked))
            Consider(match, "grouped");

        foreach (Match match in DigitsRegex().Matches(masked))
            Consider(match, "digits");

        foreach (Match match in MixedRegex().Matches(text))
            Consider(match, "mixed");

        found.Sort(static (a, b) => a.Start.CompareTo(b.Start));
        for (var i = 0; i < found.Count; i++)
        {
            var code = found[i];
            var rival = found.Any(other => other.Code != code.Code && other.Score > code.Score - ConfidentMargin);
            found[i] = code with { IsConfident = code.Score >= ConfidentScore && !rival };
        }

        return found;

        void Consider(Match match, string shape)
        {
            var (start, length) = (match.Index, match.Length);
            if (excluded?.Invoke(start, length) == true || taken.Any(t => start < t.End && t.Start < start + length))
                return;

            var score = Score(normal, start, length, shape, context, warned);
            if (score < AcceptScore)
                return;

            taken.Add((start, start + length));
            found.Add(new OtpCode(start, length, new string(match.Value.Where(char.IsLetterOrDigit).ToArray()), score, false));
        }
    }

    // A mixed code ("X7K2P9") is only believed when the message also warns about it or says how long it lasts: "use code WELCOME5" is a sale.
    private static double Score(string text, int start, int length, string shape, double context, bool warned)
    {
        var end = start + length;
        var sentenceStart = SentenceStart(text, start);
        var sentenceEnd = SentenceEnd(text, end);

        if ((end < text.Length && text[end] == '%') || (start > 0 && text[start - 1] is '$' or '€' or '£' or '¥'))
            return 0;

        var after = WordsAfter(text, end, sentenceEnd);
        if (after.Count > 0 && Unit.Contains(after[0]))
            return 0;

        var before = WordsBefore(text, sentenceStart, start);
        var labelBefore = LabelBefore(before, out var rejected);
        if (rejected)
            return 0;

        var labelAfter = shape == "digits" ? LabelAfter(after) : 0;
        if (shape != "digits" && labelBefore > 0 && GapOf(before) > 2)
            return 0;

        if (shape == "mixed" && (!warned || labelBefore <= 0 || GapOf(before) > 1))
            return 0;

        var label = Math.Max(labelBefore, labelAfter);
        if (label <= 0)
            return 0;

        var score = label + (labelBefore > 0 && labelAfter > 0 ? 1 : 0) + context;
        var digits = CountDigits(text.AsSpan(start, length));
        score += digits switch { 6 => 2, 5 or 7 => 1, _ => 0 };

        if (shape == "digits" && digits == 4 && LooksLikeYear(text.AsSpan(start, length)))
            score -= 3;

        var lineStart = text.LastIndexOf('\n', Math.Max(0, start - 1)) + 1;
        var lineEnd = text.IndexOf('\n', end);
        if (lineEnd < 0)
            lineEnd = text.Length;

        if (text.AsSpan(lineStart, lineEnd - lineStart).Trim().Length == length)
            score += 2;
        else if (PrecededByColon(text, start))
            score += 1;

        return score;
    }

    // "Your code is 482913": how well the words before the number name it, and how far they are. Zero when they do not.
    private static double LabelBefore(List<string> words, out bool rejected)
    {
        rejected = false;
        if (words.Count > 0 && Classify(words[0]) == Word.Qualifier)
        {
            rejected = true;
            return 0;
        }

        var (gap, found, strong, qualifier) = (0, false, false, false);
        foreach (var word in words)
        {
            var kind = Classify(word);
            if (kind == Word.Number)
                break;

            if (!found)
            {
                if (kind is Word.Weak or Word.Strong)
                {
                    found = true;
                    strong = kind == Word.Strong;
                }
                else
                {
                    qualifier |= kind == Word.Qualifier;
                    if (++gap > MaxLabelWords)
                        break;
                }
            }
            else if (kind == Word.Strong)
            {
                strong = true;
            }
            else if (kind == Word.Qualifier)
            {
                qualifier = true;
            }
            else if (kind != Word.Weak)
            {
                break;
            }
        }

        if (!found)
            return 0;

        // "tracking code", "promo code": named, but not as a one-time code.
        if (qualifier && !strong)
        {
            rejected = true;
            return 0;
        }

        return (strong ? StrongLabel : WeakLabel) - gap * 0.5;
    }

    // "482913 is your verification code". A word that names it from behind is less sure than one in front.
    private static double LabelAfter(List<string> words)
    {
        var (gap, found, strong, qualifier) = (0, false, false, false);
        foreach (var word in words)
        {
            var kind = Classify(word);
            if (kind == Word.Number)
                break;

            if (!found)
            {
                if (kind is Word.Weak or Word.Strong)
                {
                    found = true;
                    strong = kind == Word.Strong;
                }
                else
                {
                    qualifier |= kind == Word.Qualifier;
                    if (++gap > MaxLabelWords)
                        break;
                }
            }
            else if (kind == Word.Strong)
            {
                strong = true;
            }
            else if (kind == Word.Qualifier)
            {
                qualifier = true;
            }
            else if (kind != Word.Weak)
            {
                break;
            }
        }

        if (!found || (qualifier && !strong))
            return 0;

        return (strong ? StrongLabel : WeakLabel) - LabelAfterPenalty - gap * 0.5;
    }

    // The words between the number and the nearest word that names it.
    private static int GapOf(List<string> words)
    {
        var gap = 0;
        foreach (var word in words)
        {
            if (Classify(word) is Word.Weak or Word.Strong)
                break;

            gap++;
        }

        return gap;
    }

    private static double ContextBonus(string text)
    {
        var bonus = 0;
        if (ShareWarningRegex().IsMatch(text))
            bonus++;
        if (ValidityRegex().IsMatch(text))
            bonus++;
        if (EnterRegex().IsMatch(text))
            bonus++;
        if (text.StartsWith("<#>", StringComparison.Ordinal))
            bonus++;

        bonus = Math.Min(bonus, 3);
        return PromoRegex().IsMatch(text) ? bonus - PromoPenalty : bonus;
    }

    // A sale, not a login: "use code 1234 at checkout for 20% off".
    [GeneratedRegex(@"\boff\b|discount|checkout|\bsale\b|\bsave\b|\bfree\b|\boffer\b|\bgift\b|promo|تخفیف|هدیه|رایگان|\d%|٪", RegexOptions.CultureInvariant)]
    private static partial Regex PromoRegex();

    private enum Word { None, Weak, Strong, Qualifier, Number }

    private static Word Classify(string word)
    {
        if (word.Length >= 4 && word.All(char.IsDigit))
            return Word.Number;

        if (Strong.Contains(word))
            return Word.Strong;

        if (Weak.Contains(word) || (word.StartsWith("رمز", StringComparison.Ordinal) && !word.StartsWith("رمزارز", StringComparison.Ordinal)))
            return Word.Weak;

        return Qualifier.Contains(word) ? Word.Qualifier : Word.None;
    }

    private static bool LooksLikeYear(ReadOnlySpan<char> digits) =>
        (digits[0] == '1' && digits[1] is '3' or '4' or '9') || (digits[0] == '2' && digits[1] == '0');

    private static bool PrecededByColon(string text, int start)
    {
        var i = start - 1;
        while (i >= 0 && (text[i] == ' ' || text[i] == '\t'))
            i--;

        return i >= 0 && text[i] is ':' or '=' or '#';
    }

    private static bool HasDigitRun(string text) => CountDigits(text) >= 1;

    private static int CountDigits(ReadOnlySpan<char> text)
    {
        var count = 0;
        foreach (var c in text)
        {
            if (char.IsDigit(c))
                count++;
        }

        return count;
    }

    // Latin digits, Persian letters, lower case, plain spaces: the same text with the same length, so the indexes hold.
    private static string Normalize(string text)
    {
        var chars = new char[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            chars[i] = c switch
            {
                >= '0' and <= '9' => c,
                >= 'A' and <= 'Z' => (char)(c + 32),
                'ي' => 'ی',
                'ك' => 'ک',
                '\u200c' or '\u200f' or '\u200e' or '\u00a0' or '\u202f' or '\u0640' => ' ',
                _ when c > 127 && char.IsDigit(c) => (char)('0' + (int)char.GetNumericValue(c)),
                _ => char.ToLowerInvariant(c)
            };
        }

        return new string(chars);
    }

    private static int SentenceStart(string text, int index)
    {
        for (var i = index - 1; i >= 0; i--)
        {
            if (IsSentenceEnd(text, i))
                return i + 1;
        }

        return 0;
    }

    private static int SentenceEnd(string text, int index)
    {
        for (var i = index; i < text.Length; i++)
        {
            if (IsSentenceEnd(text, i))
                return i;
        }

        return text.Length;
    }

    private static bool IsSentenceEnd(string text, int i) =>
        text[i] is '!' or '?' or '؟' or '؛' or ';' ||
        (text[i] == '.' && (i + 1 >= text.Length || char.IsWhiteSpace(text[i + 1])));

    private static string Strip(ReadOnlySpan<char> word) => word.Trim(Punctuation).ToString();

    // The words before an index, nearest first, without their punctuation.
    private static List<string> WordsBefore(string text, int from, int index)
    {
        var words = new List<string>();
        var i = index;
        while (i > from && words.Count < MaxLabelWords + 3)
        {
            while (i > from && char.IsWhiteSpace(text[i - 1]))
                i--;

            var j = i;
            while (j > from && !char.IsWhiteSpace(text[j - 1]))
                j--;

            if (j < i && Strip(text.AsSpan(j, i - j)) is { Length: > 0 } word)
                words.Add(word);

            i = j;
        }

        return words;
    }

    // The words after an index, nearest first, without their punctuation.
    private static List<string> WordsAfter(string text, int index, int to)
    {
        var words = new List<string>();
        var i = index;
        while (i < to && words.Count < MaxLabelWords + 3)
        {
            while (i < to && char.IsWhiteSpace(text[i]))
                i++;

            var j = i;
            while (j < to && !char.IsWhiteSpace(text[j]))
                j++;

            if (j > i && Strip(text.AsSpan(i, j - i)) is { Length: > 0 } word)
                words.Add(word);

            i = j;
        }

        return words;
    }
}
