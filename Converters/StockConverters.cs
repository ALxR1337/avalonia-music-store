using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace MusicApp.Converters;

/// <summary>"немає" for a zero stock count, "N шт" otherwise.</summary>
public sealed class StockLabelConverter : IValueConverter
{
    public static readonly StockLabelConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int stock ? (stock == 0 ? "немає" : $"{stock} шт") : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>True when an int is zero — drives the danger styling on stock cells.</summary>
public sealed class IntIsZeroConverter : IValueConverter
{
    public static readonly IntIsZeroConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int n && n == 0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>True when an int is greater than zero — gates in-stock-only actions.</summary>
public sealed class IntIsPositiveConverter : IValueConverter
{
    public static readonly IntIsPositiveConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int n && n > 0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
