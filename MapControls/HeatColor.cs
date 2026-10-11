using System;
using Avalonia.Media;

namespace MapControls;

/// <summary>txlogger's colour blind modes for the value scale, in its order (its settings list and stored names).</summary>
public enum ColorBlindMode
{
    Normal,
    Universal,
    Protanopia,
    Tritanopia,
    Deuteranomaly,
}

/// <summary>
/// The map palette over the value range: txlogger's green → yellow → red (T7Suite's five stops from dark green made most
/// maps look red) or one of its colour blind scales, and T7Suite's online palette.
/// </summary>
public static class HeatColor
{
    // txlogger's low → mid → high per ColorBlindMode (pkg/colors/colorinterpolation.go)
    private static readonly Color[][] Offline =
    [
        [Color.FromRgb(0, 255, 0), Color.FromRgb(255, 255, 0), Color.FromRgb(255, 0, 0)],
        [Color.FromRgb(0x21, 0x66, 0xAC), Color.FromRgb(0xF7, 0xF7, 0xF7), Color.FromRgb(0xFF, 0xA5, 0x00)],
        [Color.FromRgb(0x05, 0x71, 0xB0), Color.FromRgb(0xF7, 0xF7, 0xF7), Color.FromRgb(0x96, 0x4B, 0x00)],
        [Color.FromRgb(0x00, 0x80, 0x80), Color.FromRgb(0xF7, 0xF7, 0xF7), Color.FromRgb(0xD7, 0x30, 0x27)],
        [Color.FromRgb(0x4A, 0x90, 0xE2), Color.FromRgb(0xF5, 0xE6, 0xB3), Color.FromRgb(0x8B, 0x45, 0x13)],
    ];
    private static readonly Color[] Online = [Colors.Wheat, Colors.LightBlue, Colors.SteelBlue, Colors.Blue, Colors.DarkBlue];

    /// <summary>The colour of value over min..max; the online palette is T7Suite's and the same in every mode.</summary>
    public static Color Interpolate(double min, double max, double value, bool online = false, ColorBlindMode mode = ColorBlindMode.Normal)
    {
        double t = (value - min) / (max - min);
        if (double.IsNaN(t)) return Color.FromRgb(128, 128, 128);
        Color[] stops = online ? Online : Offline[(int)mode];
        double pos = Math.Clamp(t, 0, 1) * (stops.Length - 1);
        int i = Math.Min((int)pos, stops.Length - 2);
        return Lerp(stops[i], stops[i + 1], pos - i);
    }

    /// <summary>
    /// Text on a cell of this colour: black, or white where white has the higher contrast (WCAG), which only the colour blind
    /// scales' dark ends need. txlogger writes black on every colour; the darkest colour of the green → red scale, pure red,
    /// keeps black.
    /// </summary>
    public static IBrush TextOn(Color fill) => Luminance(fill) < 0.17913 ? Brushes.White : Brushes.Black;

    /// <summary>The colour blind mode stored under its name (txlogger's), Normal for anything else.</summary>
    public static ColorBlindMode ParseMode(string? name) =>
        Enum.TryParse(name, true, out ColorBlindMode mode) && Enum.IsDefined(mode) ? mode : ColorBlindMode.Normal;

    // WCAG 2 relative luminance; 0.17913 is where (L + 0.05) / 0.05 = 1.05 / (L + 0.05)
    private static double Luminance(Color c) => 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);

    private static double Linear(byte v)
    {
        double s = v / 255.0;
        return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }

    private static Color Lerp(Color a, Color b, double t) =>
        Color.FromRgb((byte)(a.R + t * (b.R - a.R)), (byte)(a.G + t * (b.G - a.G)), (byte)(a.B + t * (b.B - a.B)));
}
