using System;
using Avalonia.Media;

namespace MapControls;

/// <summary>
/// The map palette over the value range: txlogger's green → yellow → red (T7Suite's five stops from dark green made most
/// maps look red), and T7Suite's online palette.
/// </summary>
public static class HeatColor
{
    private static readonly Color[] Offline = [Color.FromRgb(0, 255, 0), Color.FromRgb(255, 255, 0), Color.FromRgb(255, 0, 0)];
    private static readonly Color[] Online = [Colors.Wheat, Colors.LightBlue, Colors.SteelBlue, Colors.Blue, Colors.DarkBlue];

    public static Color Interpolate(double min, double max, double value, bool online = false)
    {
        double t = (value - min) / (max - min);
        if (double.IsNaN(t)) return Color.FromRgb(128, 128, 128);
        Color[] stops = online ? Online : Offline;
        double pos = Math.Clamp(t, 0, 1) * (stops.Length - 1);
        int i = Math.Min((int)pos, stops.Length - 2);
        return Lerp(stops[i], stops[i + 1], pos - i);
    }

    private static Color Lerp(Color a, Color b, double t) =>
        Color.FromRgb((byte)(a.R + t * (b.R - a.R)), (byte)(a.G + t * (b.G - a.G)), (byte)(a.B + t * (b.B - a.B)));
}
