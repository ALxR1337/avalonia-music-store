using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace MusicApp.Converters;

// Adds the ConverterParameter (a signed number) to a double input.
// Used to derive sizes from a parent's Bounds.Width, e.g. popup width = pill width - 40.
public sealed class DoubleOffsetConverter : IValueConverter
{
    public static readonly DoubleOffsetConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double d) return value;
        if (parameter is not string s) return d;
        if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var offset))
            return d;
        var result = d + offset;
        return result < 0 ? 0 : result;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
