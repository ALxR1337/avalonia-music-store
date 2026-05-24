using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using MusicApp.Models;

namespace MusicApp.Converters;

public sealed class RepeatModeToIconConverter : IValueConverter
{
    public static readonly RepeatModeToIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value is RepeatMode.One ? "IconRepeatOne" : "IconRepeat";
        return Application.Current?.Resources[key] as Geometry;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class RepeatModeToOpacityConverter : IValueConverter
{
    public static readonly RepeatModeToOpacityConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is RepeatMode.Off ? 0.45 : 1.0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
