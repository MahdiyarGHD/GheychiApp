using System.Text.RegularExpressions;

namespace Gheychi.Core.Services;

/// <summary>A one-time code found in a message: where it is as written, and the code itself as Latin digits (or letters) only.</summary>
/// <param name="Score">How sure the detector is; only codes that passed the bar are returned.</param>
/// <param name="Kind">What the message calls it: a verification code, or a bank's dynamic password (رمز پویا).</param>
/// <param name="IsConfident">
/// Sure enough to be filled in without asking: the message names it plainly, and nothing else in it competes for being the code.
/// </param>
public readonly record struct OtpCode(int Start, int Length, string Code, double Score, bool IsConfident, OtpKind Kind = OtpKind.Verification)
{
    public int End => Start + Length;
}

public enum OtpKind
{
    /// <summary>A code that proves who the reader is: a login, a sign-up, a new device.</summary>
    Verification,

    /// <summary>The "رمز پویا" of an Iranian bank (or any dynamic password): it confirms one payment, and is typed into a bank's page.</summary>
    DynamicPassword
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
    /// <summary>How long after it arrives a code is still offered for copying: codes last a few minutes.</summary>
    public static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(10);

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
    [GeneratedRegex(@"(?<![\w.,٬٫/+*#])(?<!\d[:\-])\d{4,8}(?!\w|[.,٬٫/:\-]\d)", RegexOptions.CultureInvariant)]
    private static partial Regex DigitsRegex();

    // "123 456", "1234-5678": a code written in two groups.
    [GeneratedRegex(@"(?<![\w.,٬٫/+*#\-])(?<!\d[:\- ])\d{3,4}[ \-]\d{3,4}(?!\w|[ \-.,٬٫/:]\d)", RegexOptions.CultureInvariant)]
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

    // "رمز پویا", "رمز دوم پویا", "dynamic password": the name Iranian banks give to a payment's one-time password.
    [GeneratedRegex(@"(?:رمز|کد|پسورد|گذرواژه)\s*(?:دوم\s*)?پویا|dynamic\s*(?:password|pin|code|otp)", RegexOptions.CultureInvariant)]
    private static partial Regex DynamicNameRegex();

    // "رمز یکبار مصرف" is a bank's too, when the message is about a payment or a card.
    [GeneratedRegex(@"رمز\s*(?:دوم\s*)?یکبارمصرف", RegexOptions.CultureInvariant)]
    private static partial Regex OneTimePasswordRegex();

    [GeneratedRegex(@"بانک|تراکنش|پرداخت|خرید|کارت|شتاب|پذیرنده|bank|transaction|payment|purchase|card", RegexOptions.CultureInvariant)]
    private static partial Regex BankingRegex();

    // Two words that are one: "log in", "یک بار مصرف". Joined, with the space moved to the end so every index holds.
    [GeneratedRegex(@"\b(?:log|sign) in\b|\bone time\b|\bpass code\b|یک ?بار ?مصرف", RegexOptions.CultureInvariant)]
    private static partial Regex GlueRegex();

    // Words that name a code in a way that makes it a one-time code.
    private static readonly HashSet<string> Strong = new(StringComparer.Ordinal)
    {
        "otp", "verification", "verify", "verifying", "authentication", "authenticate", "authorization", "authorisation",
        "passcode", "2fa", "onetime", "one-time", "security", "login", "log-in", "signin", "sign-in", "confirmation",
        "confirm", "validation", "activation", "doğrulama", "verificación", "vérification", "подтверждения", "проверочный",
        "تایید", "تاییدیه", "تأیید", "احراز", "پویا", "فعال", "یکبار", "یکبارمصرف", "امنیتی", "فعالسازی", "التحقق", "تحقق",
        "موقت", "اعتبارسنجی", "temporary", "dynamic", "mfa", "mtan", "reset", "recovery", "بازیابی"
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
        "policy", "case", "call", "تماس", "پیگیری", "رهگیری", "سفارش", "تخفیف", "کوپن", "معرف", "دعوت", "پستی", "ملی", "مبلغ",
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

    // "12345 is your Instagram code": the words that only join the number to its name.
    private static readonly HashSet<string> Filler = new(StringComparer.Ordinal)
    {
        "is", "your", "the", "a", "an", "this", "our", "for", "to", "شما", "است", "ما", "تان", "خود", "برای"
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

        var normal = Glue(Normalize(text));
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
        var kind = KindOf(normal);
        for (var i = 0; i < found.Count; i++)
        {
            var code = found[i];
            var rival = found.Any(other => other.Code != code.Code && other.Score > code.Score - ConfidentMargin);
            found[i] = code with { IsConfident = code.Score >= ConfidentScore && !rival, Kind = kind };
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
                    if (!Filler.Contains(word) && ++gap > MaxLabelWords)
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
                    if (!Filler.Contains(word) && ++gap > MaxLabelWords)
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

    private static OtpKind KindOf(string text) =>
        DynamicNameRegex().IsMatch(text) || (OneTimePasswordRegex().IsMatch(text) && BankingRegex().IsMatch(text))
            ? OtpKind.DynamicPassword
            : OtpKind.Verification;

    private static string Glue(string text) =>
        GlueRegex().IsMatch(text)
            ? GlueRegex().Replace(text, static match =>
            {
                var gaps = 0;
                foreach (var c in match.Value)
                {
                    if (c == ' ')
                        gaps++;
                }

                return match.Value.Replace(" ", string.Empty) + new string(' ', gaps);
            })
            : text;

    private static double ContextBonus(string text)
    {
        double bonus = 0;
        if (ShareWarningRegex().IsMatch(text))
            bonus++;
        if (ValidityRegex().IsMatch(text))
            bonus++;
        if (EnterRegex().IsMatch(text))
            bonus++;
        if (text.StartsWith("<#>", StringComparison.Ordinal))
            bonus++;

        bonus = Math.Min(bonus, 3);
        if (SendInstructionRegex().IsMatch(text))
            bonus -= PromoPenalty;

        return PromoRegex().IsMatch(text) ? bonus - PromoPenalty : bonus;
    }

    // "کد 1234 را به شماره 30001 ارسال کنید": a number to send somewhere, not one to type.
    [GeneratedRegex(@"ارسال\s*(?:کنید|نمایید|نمائید|فرمایید)|بفرستید|send\s+(?:\w+\s+){0,2}to\s+\d{3,}", RegexOptions.CultureInvariant)]
    private static partial Regex SendInstructionRegex();

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

        // "رمزپویا", written as one word.
        if (word.StartsWith("رمز", StringComparison.Ordinal) && word.Contains("پویا", StringComparison.Ordinal))
            return Word.Strong;

        if (Weak.Contains(word) || (word.StartsWith("رمز", StringComparison.Ordinal) && !word.StartsWith("رمزارز", StringComparison.Ordinal)))
            return Word.Weak;

        if (Qualifier.Contains(word))
            return Word.Qualifier;

        // "پویای شما", "کدی": the same word with the Persian suffix.
        if (word.Length > 3 && word[^1] == 'ی')
        {
            var stem = word[..^1];
            if (Strong.Contains(stem))
                return Word.Strong;
            if (Weak.Contains(stem))
                return Word.Weak;
        }

        return Word.None;
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
        (text[i] == '.' && (i + 1 >= text.Length || (char.IsWhiteSpace(text[i + 1]) && !NumberFollows(text, i + 1))));

    // "Rs. 5000", "No. 12": the full stop belongs to the abbreviation.
    private static bool NumberFollows(string text, int from)
    {
        while (from < text.Length && char.IsWhiteSpace(text[from]))
            from++;

        return from < text.Length && char.IsDigit(text[from]);
    }

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
