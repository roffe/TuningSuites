using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace T7App.Views;

/// <summary>UpdateKnockIndicator: a negative ignition offset (knock) turns the digits red, deeper the more is pulled.</summary>
public class IgnitionOffsetConverter : IValueConverter
{
    public static readonly IgnitionOffsetConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double v && v < 0 ? new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromRgb((byte)Math.Clamp(120 - v * 12, 0, 255), 0, 0)) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>True when the bound int isn't the parameter (the bottom panel hides on the Empty tab).</summary>
public class NotEqualConverter : IValueConverter
{
    public static readonly NotEqualConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.ToString() != parameter?.ToString();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>The AutoTune button turns red while tuning.</summary>
public class AutotuneBrushConverter : IValueConverter
{
    public static readonly AutotuneBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? Brushes.Red : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
