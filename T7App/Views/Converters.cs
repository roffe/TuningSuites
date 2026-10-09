using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using T7App.ViewModels;

namespace T7App.Views;

/// <summary>gridViewSymbols_CustomDrawCell: the symbol name cell is coloured by name prefix, first match wins.</summary>
public class SymbolColorConverter : IValueConverter
{
    public static readonly SymbolColorConverter Instance = new();

    private static readonly (string[] prefixes, IBrush brush)[] Colors =
    [
        (["TorqueCal."], Brushes.Orange),
        (["BoostCal."], Brushes.OrangeRed),
        (["BFuelCal.", "Inj", "FCutCal.", "FCompCal."], Brushes.LightSteelBlue),
        (["Ign", "DI"], Brushes.LightGreen),
        (["BstKnkCal."], Brushes.LightGray),
        (["Knk"], Brushes.Plum),
        (["MAFCal."], Brushes.Yellow),
        (["Cruise"], Brushes.SandyBrown),
        (["Evap"], Brushes.Orchid),
        (["Idle"], Brushes.BurlyWood),
        (["Lambda", "O2"], Brushes.Goldenrod),
        (["Missf"], Brushes.Bisque),
        (["Purge"], Brushes.Khaki),
        (["SAI"], Brushes.GreenYellow),
        (["StartCal."], Brushes.SeaGreen),
    ];

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string name) return null;
        foreach (var (prefixes, brush) in Colors)
            foreach (string p in prefixes)
                if (name.StartsWith(p, StringComparison.Ordinal)) return brush;
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Symbol list addresses and lengths, X6 hex or decimal per ShowAddressesInHex.</summary>
public class SymbolNumberConverter : IValueConverter
{
    public static readonly SymbolNumberConverter Instance = new();

    public static bool Hex { get; set; } = true;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is IConvertible c ? SymbolFormat.Number(c.ToInt64(CultureInfo.InvariantCulture), Hex) : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

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
