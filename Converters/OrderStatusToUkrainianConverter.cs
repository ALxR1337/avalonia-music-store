using System;
using System.Globalization;
using Avalonia.Data.Converters;
using MusicApp.Models;

namespace MusicApp.Converters;

public sealed class OrderStatusToUkrainianConverter : IValueConverter
{
    public static readonly OrderStatusToUkrainianConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is OrderStatus status ? OrderStatusLabels.Ua(status) : value?.ToString() ?? string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
