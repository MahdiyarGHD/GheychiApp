using System.Globalization;

namespace Gheychi.App.Converters;

public sealed class BubbleCornerConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is not [bool outgoing, bool first, bool last])
            return new CornerRadius(18);
        const double big = 18, mid = 6, tail = 4;
        if (CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft)
            outgoing = !outgoing;
        var near = first ? big : mid;
        var bottomNear = last ? tail : mid;
        return outgoing
            ? new CornerRadius(big, near, big, bottomNear)
            : new CornerRadius(near, big, bottomNear, big);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
