using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace MusicApp.Converters;

public sealed class SuggestionKindToIconConverter : IValueConverter
{
    public static readonly SuggestionKindToIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            "track" => "IconPlay",
            "album" => "IconBox",
            "artist" => "IconUser",
            "review" => "IconStar",
            "history" => "IconClock",
            _ => "IconSearch",
        };
        if (Application.Current?.Resources.TryGetResource(key, null, out var resource) == true)
            return resource as Geometry;
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
