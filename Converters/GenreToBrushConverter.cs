using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using MusicApp.Models;

namespace MusicApp.Converters;

public sealed class GenreToBrushConverter : IValueConverter
{
    public static readonly GenreToBrushConverter Instance = new();

    private static readonly (Color Bg, Color Accent)[] Palettes =
    {
        (Color.Parse("#FF3A1B1B"), Color.Parse("#FFE05858")),
        (Color.Parse("#FF1B2E3A"), Color.Parse("#FF4FA8E0")),
        (Color.Parse("#FF2D1B3A"), Color.Parse("#FFB070E0")),
        (Color.Parse("#FF1B3A2B"), Color.Parse("#FF50C878")),
        (Color.Parse("#FF3A2C1B"), Color.Parse("#FFE0A050")),
        (Color.Parse("#FF1B3A3A"), Color.Parse("#FF50C8C8")),
        (Color.Parse("#FF3A1B2C"), Color.Parse("#FFE060A0")),
        (Color.Parse("#FF252535"), Color.Parse("#FF8A8AE0")),
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var (idx, useAccent) = ParseInput(value, parameter);
        var (bg, accent) = Palettes[Math.Abs(idx) % Palettes.Length];
        return new SolidColorBrush(useAccent ? accent : bg);
    }

    private static (int idx, bool accent) ParseInput(object? value, object? parameter)
    {
        var accent = parameter is string s && s.Equals("accent", StringComparison.OrdinalIgnoreCase);
        var idx = value switch
        {
            Genre g => g.Id != 0 ? g.Id : StableHash(g.Name),
            string str => StableHash(str),
            int i => i,
            _ => 0
        };
        return (idx, accent);
    }

    private static int StableHash(string? s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        unchecked
        {
            int h = 17;
            foreach (var c in s) h = h * 31 + c;
            return h;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
