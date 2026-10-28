using System.Globalization;
using Gheychi.Core.Services;
using Xunit;

namespace Gheychi.Core.Tests;

public class DateFormattingServiceTests
{
    private readonly DateFormattingService _sut = new();

    [Fact]
    public void FormatThreadTime_TodayEnglish_ReturnsTime()
    {
        var now = new DateTime(2026, 9, 28, 14, 30, 0);
        var timestamp = new DateTime(2026, 9, 28, 10, 42, 0);
        var result = _sut.FormatThreadTime(timestamp, now, CultureInfo.GetCultureInfo("en-US"));
        Assert.Equal("10:42 AM", result);
    }

    [Fact]
    public void FormatThreadTime_TodayPersian_ReturnsPersianDigits()
    {
        var now = new DateTime(2026, 9, 28, 14, 30, 0);
        var timestamp = new DateTime(2026, 9, 28, 10, 42, 0);
        var result = _sut.FormatThreadTime(timestamp, now, CultureInfo.GetCultureInfo("fa-IR"));
        Assert.Equal("۱۰:۴۲", result);
    }

    [Fact]
    public void FormatThreadTime_Yesterday_ReturnsLocalizedYesterday()
    {
        var now = new DateTime(2026, 9, 28, 14, 30, 0);
        var timestamp = new DateTime(2026, 9, 27, 20, 0, 0);

        Assert.Equal("Yesterday", _sut.FormatThreadTime(timestamp, now, CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal("دیروز", _sut.FormatThreadTime(timestamp, now, CultureInfo.GetCultureInfo("fa-IR")));
    }

    [Fact]
    public void FormatDateSeparator_Today_ReturnsPrefixAndTimestamp()
    {
        var now = new DateTime(2026, 9, 28, 14, 30, 0);
        var timestamp = new DateTime(2026, 9, 28, 10, 42, 0);

        Assert.Equal("Today · 10:42 AM", _sut.FormatDateSeparator(timestamp, now, CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal("امروز · ۱۰:۴۲", _sut.FormatDateSeparator(timestamp, now, CultureInfo.GetCultureInfo("fa-IR")));
    }

    [Fact]
    public void FormatDayHeader_ReturnsDayWithoutTime()
    {
        var now = new DateTime(2026, 9, 28, 14, 30, 0);
        var en = CultureInfo.GetCultureInfo("en-US");
        var fa = CultureInfo.GetCultureInfo("fa-IR");

        Assert.Equal("Today", _sut.FormatDayHeader(new DateTime(2026, 9, 28, 1, 0, 0), now, en));
        Assert.Equal("دیروز", _sut.FormatDayHeader(new DateTime(2026, 9, 27, 23, 0, 0), now, fa));
        Assert.Equal("Thu · Sep 24", _sut.FormatDayHeader(new DateTime(2026, 9, 24, 9, 0, 0), now, en));
        Assert.Equal("Dec 30, 2025", _sut.FormatDayHeader(new DateTime(2025, 12, 30, 9, 0, 0), now, en));
    }

    [Fact]
    public void FormatDayHeader_ByLanguage_PersianIsSolar()
    {
        var now = new DateTime(2026, 9, 28, 14, 30, 0);
        Assert.Equal("پنج‌شنبه · ۲ مهر", _sut.FormatDayHeader(new DateTime(2026, 9, 24, 9, 0, 0), now, CultureInfo.GetCultureInfo("fa-IR")));
    }

    [Fact]
    public void Solar_InEnglish_UsesSolarMonths()
    {
        var now = new DateTime(2026, 9, 28, 14, 30, 0);
        var en = CultureInfo.GetCultureInfo("en-US");
        WithCalendar(CalendarChoice.Solar, () =>
        {
            Assert.Equal("Thu · Mehr 2", _sut.FormatDayHeader(new DateTime(2026, 9, 24, 9, 0, 0), now, en));
            Assert.Equal("Dey 9, 1404", _sut.FormatDayHeader(new DateTime(2025, 12, 30, 9, 0, 0), now, en));
            Assert.Equal("Shahrivar 19", _sut.FormatThreadTime(new DateTime(2026, 9, 10, 9, 0, 0), now, en));
            Assert.Equal("1404/10/9", _sut.FormatThreadTime(new DateTime(2025, 12, 30, 9, 0, 0), now, en));
        });
    }

    [Fact]
    public void Gregorian_InPersian_UsesGregorianMonthsAndPersianDigits()
    {
        var now = new DateTime(2026, 9, 28, 14, 30, 0);
        var fa = CultureInfo.GetCultureInfo("fa-IR");
        WithCalendar(CalendarChoice.Gregorian, () =>
        {
            Assert.Equal("پنج‌شنبه · ۲۴ سپتامبر", _sut.FormatDayHeader(new DateTime(2026, 9, 24, 9, 0, 0), now, fa));
            Assert.Equal("۳۰ دسامبر ۲۰۲۵", _sut.FormatDayHeader(new DateTime(2025, 12, 30, 9, 0, 0), now, fa));
            Assert.Equal("۲۰۲۵/۱۲/۳۰", _sut.FormatThreadTime(new DateTime(2025, 12, 30, 9, 0, 0), now, fa));
        });
    }

    // The calendar is app-wide; put it back so the other tests see the default.
    private static void WithCalendar(CalendarChoice calendar, Action test)
    {
        DateFormattingService.Calendar = calendar;
        try
        {
            test();
        }
        finally
        {
            DateFormattingService.Calendar = CalendarChoice.Language;
        }
    }

    [Fact]
    public void ToPersianDigits_ConvertsCorrectly()
    {
        var result = DateFormattingService.ToPersianDigits("0123456789");
        Assert.Equal("۰۱۲۳۴۵۶۷۸۹", result);
    }
}
