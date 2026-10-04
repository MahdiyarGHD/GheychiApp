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
    public void ToPersianDigits_ConvertsCorrectly()
    {
        var result = DateFormattingService.ToPersianDigits("0123456789");
        Assert.Equal("۰۱۲۳۴۵۶۷۸۹", result);
    }
}
