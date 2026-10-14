using System.Collections.Generic;
using System.Linq;
using CommonSuite;
using MapControls;
using Trionic5Tools;

namespace T5App.ViewModels;

/// <summary>T5Suite's own tools: the dyno graph, the compressor map and the injection timing viewer, over T5InjectionModel.</summary>
public partial class T5Module
{
    /// <summary>Show dyno graph: per rpm the WOT boost, torque, power and injector duty cycle.</summary>
    public List<(int Rpm, double Boost, double Torque, double Power, double DutyCycle)> Dyno() => T5 is { } bin ? T5InjectionModel.Dyno(bin) : [];

    /// <summary>
    /// The compressor map's first guess from the footer's turbo type and the part number (frmMain 11709): T25s by car model, the
    /// TD04-15G on Aero; the displacement in cubic inches (140 for a 2.3 litre, else 122).
    /// </summary>
    public (int Compressor, double CubicInches) CompressorDefaults()
    {
        if (T5 is not { } bin) return (CompressorMap.T25Trim60, 122);
        Trionic5Properties p = bin.File.GetTrionicProperties();
        ECUInformation ecu = new PartNumberConverter().GetECUInfo(p.Partnumber.Trim(), p.Enginetype.Trim());
        double cid = ecu.Valid && ecu.Is2point3liter ? 140 : 122;
        int ByModel(bool aero) => !ecu.Valid ? CompressorMap.T25Trim60 : aero && ecu.Isaero ? CompressorMap.Td04_15G
            : ecu.Carmodel is CarModel.Saab900 or CarModel.Saab900SE or CarModel.Saab93 ? CompressorMap.T25Ng900 : CompressorMap.T25Trim60;
        int compressor = p.TurboType switch
        {
            TurboType.GT28BB or TurboType.GT28RS => CompressorMap.Gt28Rs,
            TurboType.Stock => ByModel(true),
            TurboType.TD0415T => CompressorMap.Td04_15G,
            TurboType.GT3071R => CompressorMap.Gt3071R,
            TurboType.HX40w => CompressorMap.Hx40w,
            TurboType.TD0419T => CompressorMap.Td04_19T,
            TurboType.S400SX371 => CompressorMap.S400,
            _ => ByModel(false),
        };
        return (compressor, cid);
    }

    /// <summary>The compressor map's curve: the dyno's WOT boost per rpm through the T5 formula.</summary>
    public List<(double lbmin, double pr)> CompressorPoints(double cubicInches, double tempC, IReadOnlyList<double> vePercent)
    {
        var dyno = Dyno();
        return CompressorMap.PointsFromBoost(dyno.Select(d => d.Rpm).ToList(), dyno.Select(d => d.Boost).ToList(), cubicInches, vePercent, tempC);
    }

    /// <summary>The injection timing viewer's maps: Normal (Insp_mat!), Idle (Idle_fuel_korr!), Knocking (Fuel_knock_mat!).</summary>
    public static IReadOnlyList<(string Caption, string Map)> InjectionConditions { get; } =
        [("Normal", "Insp_mat!"), ("Idle", "Idle_fuel_korr!"), ("Knocking", "Fuel_knock_mat!")];

    /// <summary>
    /// Fuel injection timing (frmInjectionTiming): per cell of the chosen map the injection time (ms) or duty cycle (%) at the cell's
    /// load and rpm, the chosen intake temperature, battery voltage and injector constant, to two decimals; the load axis in bar.
    /// Nothing is written.
    /// </summary>
    public MapData? InjectionTiming(string mapName, int iatAd, int volt, int injectorConstant, bool dutyCycle)
    {
        if (T5 is not { } bin || bin.Find(mapName) is not { } sh || bin.ReadSymbol(sh) is not { Length: > 0 } map) return null;
        int[] x = bin.GetXaxisValues(mapName), y = bin.GetYaxisValues(mapName);
        if (x.Length == 0 || y.Length == 0) return null;
        var model = new T5InjectionModel(bin) { IatAd = iatAd, BatteryVolt = volt, InjectorConstant = injectorConstant };
        int percent = bin.SensorPercent;
        var data = new byte[x.Length * y.Length * 2];
        var text = new string[x.Length * y.Length];
        for (int r = 0; r < y.Length; r++)
            for (int c = 0; c < x.Length; c++)
            {
                double ms = model.Milliseconds(x[c] * percent / 100, y[r], map, x, y);
                // T5Suite printed the float with F2: seven digits, halves away from zero, as decimal rounds it; the text keeps
                // duty cycles above 655.35 % that the 16-bit cell (the colour scale) caps
                float value = (float)(dutyCycle ? T5InjectionModel.DutyCycle(ms, y[r]) : ms);
                decimal hundredths = System.Math.Round((decimal)value * 100, System.MidpointRounding.AwayFromZero);
                text[r * x.Length + c] = (hundredths / 100).ToString("0.##", System.Globalization.CultureInfo.CurrentCulture);
                int v = (int)System.Math.Clamp(hundredths, 0, 0xFFFF);
                data[(r * x.Length + c) * 2] = (byte)(v >> 8);
                data[(r * x.Length + c) * 2 + 1] = (byte)v;
            }
        return new MapData(mapName, data, x.Length, true, 0xFFFF)
        {
            Factor = 0.01,
            // CustomDrawCell showed that text parsed back, without trailing zeros (40.8, 1)
            EasyFormat = "0.##",
            EasyText = i => text[i],
            UpsideDown = true,
            // the headers in bar from the float MAP value (× 1.2 for 3.0 bar), F2 (frmInjectionTiming's CustomDrawColumnHeader)
            XAxis = x.Select(v => (double)(v * percent / 100)).ToArray(),
            XEasy = bin.XAxisEasy(mapName),
            YAxis = y.Select(v => (double)v).ToArray(),
            XName = "bar",
            YName = "rpm",
            ZName = dutyCycle ? "DC %" : "ms",
        };
    }

    /// <summary>The file's injector constant (the first byte of Inj_konst!).</summary>
    public int InjectorConstant => T5 is { } bin ? new T5InjectionModel(bin).InjectorConstant : 21;
}
