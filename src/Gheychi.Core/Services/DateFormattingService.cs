using System.Globalization;

namespace Gheychi.Core.Services;

/// <summary>The calendar dates are shown in.</summary>
public enum CalendarChoice
{
    /// <summary>The language's own: solar (Jalali) in Persian, Gregorian otherwise.</summary>
    Language,
    Solar,
    Gregorian,
}

public sealed class DateFormattingService : IDateFormattingService
{
    private static readonly string[] PersianMonths =
    [
        "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور",
        "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"
    ];

    private static readonly string[] SolarMonthsLatin =
    [
        "Farvardin", "Ordibehesht", "Khordad", "Tir", "Mordad", "Shahrivar",
        "Mehr", "Aban", "Azar", "Dey", "Bahman", "Esfand"
    ];

    private static readonly string[] GregorianMonthsPersian =
    [
        "ژانویه", "فوریه", "مارس", "آوریل", "مه", "ژوئن",
        "ژوئیه", "اوت", "سپتامبر", "اکتبر", "نوامبر", "دسامبر"
    ];

    private static readonly PersianCalendar Pc = new();

    /// <summary>The calendar picked in Settings. Set at startup; a change is applied by restarting the app.</summary>
    public static CalendarChoice Calendar { get; set; }

    /// <summary>Whether dates for <paramref name="culture"/> are shown in the solar calendar.</summary>
    public static bool UsesSolar(CultureInfo culture) => Calendar switch
    {
        CalendarChoice.Solar => true,
        CalendarChoice.Gregorian => false,
        _ => IsPersian(culture),
    };

    /// <summary>
    /// The name of <paramref name="month"/> (1-12) of the solar or Gregorian year, in Persian or English. English
    /// Gregorian months are abbreviated ("Sep"); the solar ones have no common short form.
    /// </summary>
    public static string MonthName(int month, bool solar, bool persian) => (solar, persian) switch
    {
        (true, true) => PersianMonths[month - 1],
        (true, false) => SolarMonthsLatin[month - 1],
        (false, true) => GregorianMonthsPersian[month - 1],
        _ => CultureInfo.InvariantCulture.DateTimeFormat.AbbreviatedMonthNames[month - 1],
    };

    private static bool IsPersian(CultureInfo culture) => culture.Name.StartsWith("fa", StringComparison.OrdinalIgnoreCase);

    private static (int Year, int Month, int Day) DateParts(DateTime date, bool solar) =>
        solar ? (Pc.GetYear(date), Pc.GetMonth(date), Pc.GetDayOfMonth(date)) : (date.Year, date.Month, date.Day);

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

        var solar = UsesSolar(culture);
        var (year, month, day) = DateParts(timestamp, solar);
        var sameYear = year == DateParts(now, solar).Year;
        var monthName = MonthName(month, solar, isFa);

        if (isFa)
        {
            return sameYear
                ? $"{ToPersianDigits(day.ToString(CultureInfo.InvariantCulture))} {monthName}"
                : ToPersianDigits($"{year}/{month:D2}/{day:D2}");
        }

        return sameYear
            ? $"{monthName} {day}"
            : $"{year}/{month}/{day}";
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

        return FormatDay(timestamp, now, isFa, UsesSolar(culture));
    }

    public string FormatDayHeader(DateTime timestamp, DateTime now, CultureInfo culture)
    {
        var isFa = culture.Name.StartsWith("fa", StringComparison.OrdinalIgnoreCase);
        return (now.Date - timestamp.Date).TotalDays switch
        {
            0 => isFa ? "امروز" : "Today",
            1 => isFa ? "دیروز" : "Yesterday",
            _ => FormatDay(timestamp, now, isFa, UsesSolar(culture))
        };
    }

    private static string FormatDay(DateTime timestamp, DateTime now, bool isFa, bool solar)
    {
        var (year, month, day) = DateParts(timestamp, solar);
        var sameYear = year == DateParts(now, solar).Year;
        var monthName = MonthName(month, solar, isFa);

        if (isFa)
        {
            var dayName = GetPersianDayOfWeek(timestamp.DayOfWeek);

            return sameYear
                ? $"{dayName} · {ToPersianDigits(day.ToString(CultureInfo.InvariantCulture))} {monthName}"
                : $"{ToPersianDigits(day.ToString(CultureInfo.InvariantCulture))} {monthName} {ToPersianDigits(year.ToString(CultureInfo.InvariantCulture))}";
        }

        return sameYear
            ? $"{timestamp.ToString("ddd", CultureInfo.InvariantCulture)} · {monthName} {day}"
            : $"{monthName} {day}, {year}";
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
