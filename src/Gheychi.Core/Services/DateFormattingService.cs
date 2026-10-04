using System.Globalization;

namespace Gheychi.Core.Services;

public sealed class DateFormattingService : IDateFormattingService
{
    private static readonly string[] PersianMonths =
    [
        "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور",
        "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"
    ];

    private static readonly PersianCalendar Pc = new();

    public string FormatThreadTime(DateTime timestamp, DateTime now, CultureInfo culture)
    {
        var isFa = culture.Name.StartsWith("fa", StringComparison.OrdinalIgnoreCase);
        var diffDays = (now.Date - timestamp.Date).TotalDays;

        if (diffDays == 0)
        {
            return isFa
                ? ToPersianDigits(timestamp.ToString("HH:mm", CultureInfo.InvariantCulture))
                : timestamp.ToString("h:mm tt", CultureInfo.InvariantCulture);
        }

        if (diffDays == 1)
        {
            return isFa ? "دیروز" : "Yesterday";
        }

        if (diffDays is > 1 and < 7)
        {
            return isFa
                ? GetPersianDayOfWeek(timestamp.DayOfWeek)
                : timestamp.ToString("ddd", CultureInfo.InvariantCulture);
        }

        if (isFa)
        {
            var year = Pc.GetYear(timestamp);
            var nowYear = Pc.GetYear(now);
            var month = Pc.GetMonth(timestamp);
            var day = Pc.GetDayOfMonth(timestamp);

            return year == nowYear
                ? $"{ToPersianDigits(day.ToString(CultureInfo.InvariantCulture))} {PersianMonths[month - 1]}"
                : ToPersianDigits($"{year}/{month:D2}/{day:D2}");
        }

        return timestamp.Year == now.Year
            ? timestamp.ToString("MMM d", CultureInfo.InvariantCulture)
            : timestamp.ToString("yyyy/M/d", CultureInfo.InvariantCulture);
    }

    public string FormatDateSeparator(DateTime timestamp, DateTime now, CultureInfo culture)
    {
        var isFa = culture.Name.StartsWith("fa", StringComparison.OrdinalIgnoreCase);
        var diffDays = (now.Date - timestamp.Date).TotalDays;

        if (diffDays == 0)
        {
            var time = isFa
                ? ToPersianDigits(timestamp.ToString("HH:mm", CultureInfo.InvariantCulture))
                : timestamp.ToString("hh:mm tt", CultureInfo.InvariantCulture);
            return isFa ? $"امروز · {time}" : $"Today · {time}";
        }

        if (diffDays == 1)
        {
            var time = isFa
                ? ToPersianDigits(timestamp.ToString("HH:mm", CultureInfo.InvariantCulture))
                : timestamp.ToString("hh:mm tt", CultureInfo.InvariantCulture);
            return isFa ? $"دیروز · {time}" : $"Yesterday · {time}";
        }

        if (isFa)
        {
            var year = Pc.GetYear(timestamp);
            var nowYear = Pc.GetYear(now);
            var month = Pc.GetMonth(timestamp);
            var day = Pc.GetDayOfMonth(timestamp);
            var dayName = GetPersianDayOfWeek(timestamp.DayOfWeek);

            return year == nowYear
                ? $"{dayName} · {ToPersianDigits(day.ToString(CultureInfo.InvariantCulture))} {PersianMonths[month - 1]}"
                : $"{ToPersianDigits(day.ToString(CultureInfo.InvariantCulture))} {PersianMonths[month - 1]} {ToPersianDigits(year.ToString(CultureInfo.InvariantCulture))}";
        }

        return timestamp.Year == now.Year
            ? timestamp.ToString("ddd · MMM d", CultureInfo.InvariantCulture)
            : timestamp.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
    }

    public string FormatStickyDate(DateTime timestamp, DateTime now, CultureInfo culture) =>
        FormatDateSeparator(timestamp, now, culture);

    public string FormatMessageTime(DateTime timestamp, CultureInfo culture)
    {
        var isFa = culture.Name.StartsWith("fa", StringComparison.OrdinalIgnoreCase);
        return isFa
            ? ToPersianDigits(timestamp.ToString("HH:mm", CultureInfo.InvariantCulture))
            : timestamp.ToString("hh:mm tt", CultureInfo.InvariantCulture);
    }

    public static string ToPersianDigits(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        Span<char> chars = stackalloc char[input.Length];
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            chars[i] = c is >= '0' and <= '9' ? (char)(c - '0' + 0x06F0) : c;
        }

        return new string(chars);
    }

    private static string GetPersianDayOfWeek(DayOfWeek day) =>
        day switch
        {
            DayOfWeek.Saturday => "شنبه",
            DayOfWeek.Sunday => "یکشنبه",
            DayOfWeek.Monday => "دوشنبه",
            DayOfWeek.Tuesday => "سه‌شنبه",
            DayOfWeek.Wednesday => "چهارشنبه",
            DayOfWeek.Thursday => "پنج‌شنبه",
            DayOfWeek.Friday => "جمعه",
            _ => string.Empty
        };
}
