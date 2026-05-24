using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using MusicApp.Models;

namespace MusicApp.Converters;

public sealed class RepeatModeNotOffConverter : IValueConverter
{
    public static readonly RepeatModeNotOffConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is RepeatMode rm && rm != RepeatMode.Off;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class BoolToFontWeightConverter : IValueConverter
{
    public static readonly BoolToFontWeightConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? FontWeight.SemiBold : FontWeight.Normal;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class BoolToCurrentForegroundConverter : IValueConverter
{
    public static readonly BoolToCurrentForegroundConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isCurrent = value is bool b && b;
        var key = isCurrent ? "AccentBrush" : "FgPrimaryBrush";
        if (Application.Current?.Resources.TryGetResource(key, null, out var resource) == true)
            return resource as IBrush;
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
