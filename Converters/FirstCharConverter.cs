using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace MusicApp.Converters;

public sealed class FirstCharConverter : IValueConverter
{
    public static readonly FirstCharConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s)
            return string.Empty;

        var trimmed = s.TrimStart();
        return trimmed.Length == 0
            ? string.Empty
            : char.ToUpperInvariant(trimmed[0]).ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
