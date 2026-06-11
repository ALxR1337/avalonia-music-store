using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace MusicApp.Converters;

/// <summary>
/// Bridges a plain <see cref="DateTime"/> view-model property to Avalonia's
/// <see cref="Avalonia.Controls.DatePicker"/>, whose <c>SelectedDate</c> is a
/// nullable <see cref="DateTimeOffset"/>. Binding a <see cref="DateTime"/>
/// directly throws <c>InvalidCastException</c>.
/// </summary>
public sealed class DateTimeOffsetConverter : IValueConverter
{
    public static readonly DateTimeOffsetConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        // Strip Kind so the DateTimeOffset ctor never rejects a Local/Utc mismatch.
        => value is DateTime dt
            ? new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Unspecified), TimeSpan.Zero)
            : (DateTimeOffset?)null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is DateTimeOffset dto ? dto.Date : null;
}
