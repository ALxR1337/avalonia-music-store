using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace MusicApp.Converters;

// "collapsedWidth|expandedWidth", e.g. "72|240".
// When value (bool) is true → collapsedWidth, otherwise expandedWidth.
public sealed class BoolToWidthConverter : IValueConverter
{
    public static readonly BoolToWidthConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var collapsed = value is bool b && b;
        if (parameter is string s)
        {
            var parts = s.Split('|');
            if (parts.Length == 2
                && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var w0)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var w1))
            {
                return collapsed ? w0 : w1;
            }
        }
        return collapsed ? 72.0 : 240.0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
