using System;
using Avalonia.Media;

namespace MapControls;

/// <summary>T7Suite's 3D/graph palette: five stops at 0/25/50/75/100 % of the value range, interpolated in between.</summary>
public static class HeatColor
{
    private static readonly Color[] Offline = [Colors.Green, Colors.Yellow, Colors.Orange, Colors.OrangeRed, Colors.Red];
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
