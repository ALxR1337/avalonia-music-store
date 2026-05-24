using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace MusicApp.Converters;

public sealed class BoolToHighlightBrushConverter : IValueConverter
{
    private static readonly IBrush HighlightBrush =
        new SolidColorBrush(Color.FromArgb(0x33, 0xE0, 0x7B, 0x39));
    public static readonly BoolToHighlightBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? HighlightBrush : Brushes.Transparent;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
