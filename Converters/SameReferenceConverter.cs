using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace MusicApp.Converters;

/// <summary>
/// True when both inputs are the same object instance — drives per-row state
/// that lives in a single VM property (e.g. "which row is awaiting delete
/// confirmation"). The inverted instance feeds the idle-state controls.
/// </summary>
public sealed class SameReferenceConverter : IMultiValueConverter
{
    public static readonly SameReferenceConverter Instance = new(invert: false);
    public static readonly SameReferenceConverter Inverted = new(invert: true);

    private readonly bool _invert;

    private SameReferenceConverter(bool invert) => _invert = invert;

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var same = values.Count >= 2 && values[0] is not null && ReferenceEquals(values[0], values[1]);
        return same ^ _invert;
    }
}
