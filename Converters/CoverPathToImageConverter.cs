using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace MusicApp.Converters;

public sealed class CoverPathToImageConverter : IValueConverter
{
    public static readonly CoverPathToImageConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            var uri = path.StartsWith("avares://", StringComparison.Ordinal)
                ? new Uri(path)
                : new Uri($"avares://MusicApp/{path.TrimStart('/', '\\')}");

            if (AssetLoader.Exists(uri))
                return new Bitmap(AssetLoader.Open(uri));
        }
        catch
        {
        }

        try
        {
            if (File.Exists(path))
                return new Bitmap(path);
        }
        catch
        {
        }

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
