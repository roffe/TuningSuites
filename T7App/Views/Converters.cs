using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace T7App.Views;

/// <summary>CompareResults rows: Salmon when missing in the original file, CornflowerBlue when missing in the compare file.</summary>
public class CompareRowColorConverter : IValueConverter
{
    public static readonly CompareRowColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        T7.CompareRow { MissingInOriFile: true } => Brushes.Salmon,
        T7.CompareRow { MissingInCompareFile: true } => Brushes.CornflowerBlue,
        _ => null,
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

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

/// <summary>Import results: Success fades from green, Fail from red, as frmImportResults' gradient cells.</summary>
public class ResultBrushConverter : IValueConverter
{
    public static readonly ResultBrushConverter Instance = new();

    private static IBrush Fade(Color c) => new Avalonia.Media.Immutable.ImmutableLinearGradientBrush(
        [new Avalonia.Media.Immutable.ImmutableGradientStop(0, c), new Avalonia.Media.Immutable.ImmutableGradientStop(1, Colors.White)],
        startPoint: new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative), endPoint: new Avalonia.RelativePoint(1, 0, Avalonia.RelativeUnit.Relative));

    private static readonly IBrush Ok = Fade(Colors.LimeGreen), Failed = Fade(Colors.IndianRed);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? Ok : Failed;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public class ResultTextConverter : IValueConverter
{
    public static readonly ResultTextConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? "Success" : "Fail";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
