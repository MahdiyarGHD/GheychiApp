using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Gheychi.App.Ui;

/// <summary>Finds an icon geometry from the image name a view model carries (<c>"chevron_right.png"</c> -> <c>Icon.ChevronRight</c>).</summary>
public static class IconCatalog
{
    private static readonly Dictionary<string, Geometry?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static Geometry? Find(string? imageName)
    {
        if (string.IsNullOrEmpty(imageName))
            return null;

        lock (Cache)
        {
            if (Cache.TryGetValue(imageName, out var known))
                return known;

            var stem = Path.GetFileNameWithoutExtension(imageName);
            var key = "Icon." + string.Concat(stem.Split('_').Select(part => part.Length == 0 ? part : char.ToUpperInvariant(part[0]) + part[1..]));
            Geometry? geometry = null;
            if (Application.Current?.TryFindResource(key, out var resource) == true)
                geometry = resource as Geometry;

            return Cache[imageName] = geometry;
        }
    }
}

/// <summary><c>Data="{Binding IconFile, Converter={x:Static ui:IconFileConverter.Instance}}"</c></summary>
public sealed class IconFileConverter : IValueConverter
{
    public static readonly IconFileConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        IconCatalog.Find(value as string);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
