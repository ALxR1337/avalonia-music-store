using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using MusicApp.Models;

namespace MusicApp.Converters;

/// <summary>
/// True when both inputs resolve to the same order (matched by Id) — drives the
/// inline expansion of an order row in the profile. The inverted instance feeds
/// the collapsed-state button label.
/// </summary>
public sealed class SameOrderConverter : IMultiValueConverter
{
    public static readonly SameOrderConverter Instance = new(invert: false);
    public static readonly SameOrderConverter Inverted = new(invert: true);

    private readonly bool _invert;

    private SameOrderConverter(bool invert) => _invert = invert;

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var same = values is [Order a, Order b, ..] && a.Id == b.Id;
        return same ^ _invert;
    }
}
