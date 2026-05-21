using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using MusicApp.Models;

namespace MusicApp.Converters;

public sealed class AlbumIdToGradientBrushConverter : IValueConverter
{
    public static readonly AlbumIdToGradientBrushConverter Instance = new();

    private static readonly (Color From, Color To)[] Palettes =
    {
        (Color.Parse("#FF3A2D1E"), Color.Parse("#FF1A1A1A")),
        (Color.Parse("#FF1A3A4A"), Color.Parse("#FF101010")),
        (Color.Parse("#FF3A1E1E"), Color.Parse("#FF1A0F0F")),
        (Color.Parse("#FF1E3A26"), Color.Parse("#FF0F1A14")),
        (Color.Parse("#FF2E1E3A"), Color.Parse("#FF14101F")),
        (Color.Parse("#FF3A301E"), Color.Parse("#FF1A1610")),
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var idx = value switch
        {
            Album a => a.Id,
            int i => i,
            _ => 0
        };

        var (from, to) = Palettes[Math.Abs(idx) % Palettes.Length];
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(from, 0),
                new GradientStop(to, 1)
            }
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
