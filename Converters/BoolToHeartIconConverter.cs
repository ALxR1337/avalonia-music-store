using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace MusicApp.Converters;

public sealed class BoolToHeartIconConverter : IValueConverter
{
    public static readonly BoolToHeartIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var liked = value is bool b && b;
        var key = liked ? "IconHeartFilled" : "IconHeart";
        if (Application.Current?.Resources.TryGetResource(key, null, out var resource) == true)
            return resource as Geometry;
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
