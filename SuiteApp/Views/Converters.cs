using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using SuiteApp.ViewModels;

namespace SuiteApp.Views;

/// <summary>gridViewSymbols_CustomDrawCell: the symbol name cell is coloured by name prefix, first match wins.</summary>
public class SymbolColorConverter : IValueConverter
{
    public static readonly SymbolColorConverter Instance = new();

    /// <summary>T8Suite's list had no name colours: the suite's view model switches them off.</summary>
    public static bool Enabled { get; set; } = true;

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
        if (!Enabled || value is not string name) return null;
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

/// <summary>
/// Whether the symbol name's category colour is light (parameter "light", black text) or dark ("dark", white text). Without a
/// colour both classes are off and the text keeps the theme's inherited colour (a binding's UnsetValue would reset it to black).
/// </summary>
public class SymbolShadeConverter : IValueConverter
{
    public static readonly SymbolShadeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        SymbolColorConverter.Instance.Convert(value, targetType, null, culture) is ISolidColorBrush { Color: var c } &&
        (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B > 110) == (parameter as string == "light");

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
