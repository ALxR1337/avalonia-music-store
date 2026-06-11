using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace MusicApp.Converters;

// "{0} {форма}" with the correct Ukrainian plural form. Parameter carries the
// three forms separated by '|': "позиція|позиції|позицій" → 1 позиція,
// 2 позиції, 5 позицій, 21 позиція…
public sealed class UkrainianPluralConverter : IValueConverter
{
    public static string Choose(long n, string one, string few, string many)
    {
        var abs = Math.Abs(n);
        var mod10 = abs % 10;
        var mod100 = abs % 100;
        if (mod10 == 1 && mod100 != 11) return one;
        if (mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14) return few;
        return many;
    }

    public static string Format(long n, string one, string few, string many) =>
        $"{n} {Choose(n, one, few, many)}";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is not string spec) return null;
        var forms = spec.Split('|');
        if (forms.Length != 3) return value.ToString();

        long n;
        try { n = System.Convert.ToInt64(value, culture); }
        catch { return value.ToString(); }

        return Format(n, forms[0], forms[1], forms[2]);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
