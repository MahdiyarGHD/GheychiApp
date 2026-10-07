using System.Globalization;

namespace Gheychi.Core.Services;

public interface IDateFormattingService
{
    string FormatThreadTime(DateTime timestamp, DateTime now, CultureInfo culture);
    string FormatDateSeparator(DateTime timestamp, DateTime now, CultureInfo culture);
    /// <summary>The day alone ("Today", "Yesterday", "Mon · Oct 5"), for lists grouped by day.</summary>
    string FormatDayHeader(DateTime timestamp, DateTime now, CultureInfo culture);
    string FormatMessageTime(DateTime timestamp, CultureInfo culture);
    string FormatStickyDate(DateTime timestamp, DateTime now, CultureInfo culture);
}
