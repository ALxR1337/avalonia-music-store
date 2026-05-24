using System;
using System.Globalization;
using Avalonia.Data.Converters;
using MusicApp.Models;

namespace MusicApp.Converters;

public sealed class OrderStatusToUkrainianConverter : IValueConverter
{
    public static readonly OrderStatusToUkrainianConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            OrderStatus.New => "Нове",
            OrderStatus.Processing => "В обробці",
            OrderStatus.Completed => "Виконано",
            OrderStatus.Cancelled => "Скасовано",
            _ => value?.ToString() ?? string.Empty
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
