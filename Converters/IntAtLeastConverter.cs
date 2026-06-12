using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace MusicApp.Converters;

/// <summary>
/// True when an int value is ≥ the integer in ConverterParameter — drives the
/// filled state of the clickable rating stars in the review editor.
/// </summary>
public sealed class IntAtLeastConverter : IValueConverter
{
    public static readonly IntAtLeastConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int n
        && parameter is string s && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var min)
        && n >= min;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
