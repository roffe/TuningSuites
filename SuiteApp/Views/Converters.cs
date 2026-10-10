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

/// <summary>T5Suite's description cell by XDF category (gridViewSymbols_CustomDrawCell); the suite's view model switches it on.</summary>
public class CategoryColorConverter : IValueConverter
{
    public static readonly CategoryColorConverter Instance = new();

    public static bool Enabled { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        IBrush? brush = !Enabled ? null : (value as string) switch
        {
            "Fuel" => Brushes.LightSteelBlue,
            "Ignition" => Brushes.LightGreen,
            "Boost_control" => Brushes.OrangeRed,
            "Misc" => Brushes.LightGray,
            "Sensor" => Brushes.Yellow,
            "Correction" => Brushes.LightPink,
            "Idle" => Brushes.BurlyWood,
            _ => null,
        };
        // "light": a coloured cell, which takes dark text
        return parameter as string == "light" ? brush != null : brush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>CompareResults rows: Salmon when missing in the original file, CornflowerBlue when missing in the compare file.</summary>
public class CompareRowColorConverter : IValueConverter
{
    public static readonly CompareRowColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        CommonSuite.CompareRow { MissingInOriFile: true } => Brushes.Salmon,
        CommonSuite.CompareRow { MissingInCompareFile: true } => Brushes.CornflowerBlue,
        _ => null,
    };

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

/// <summary>Text shown as a menu header: a single underscore marks the access key there, so names keep theirs doubled.</summary>
public class MenuTextConverter : IValueConverter
{
    public static readonly MenuTextConverter Instance = new();

    public static string Escape(string text) => text.Replace("_", "__");

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is string s ? Escape(s) : value;

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

