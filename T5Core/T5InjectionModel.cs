using System;
using System.Collections.Generic;
using CommonSuite;

namespace Trionic5Tools
{
    /// <summary>
    /// T5Suite's model of the ECU's injection time (frmDynoChart and frmInjectionTiming held the same copy): the air temperature
    /// compensation, the fuel map cell at load × rpm ("Trionic style" interpolation), the injector constant, the battery correction
    /// and Min_tid!; time in 1/250 ms.
    /// </summary>
    public sealed class T5InjectionModel
    {
        private readonly int[] m_luftKompfak, m_lufttempSteg, m_battKorr;
        private readonly int m_minTid;

        /// <summary>The injector constant: the first byte of Inj_konst! (the injection timing viewer lets you try others).</summary>
        public int InjectorConstant { get; set; }

        /// <summary>The intake air temperature as the ECU's AD value (163 ≈ 20 °C).</summary>
        public int IatAd { get; set; } = 163;

        public int BatteryVolt { get; set; } = 13;

        public T5InjectionModel(T5Binary bin)
        {
            Trionic5File f = bin.File;
            m_luftKompfak = f.Luft_kompfak_array ?? [];
            m_lufttempSteg = f.Lufttemp_steg_array ?? [];
            m_battKorr = f.GetSymbolAsIntArray(bin.Info.GetBatteryCorrectionMap()) ?? [];
            m_minTid = f.GetSymbolAsInt("Min_tid!");
            InjectorConstant = bin.Find(bin.Info.GetInjectorConstant()) is { } sh && bin.ReadSymbol(sh) is { Length: > 0 } k ? k[0] : 21;
        }

        /// <summary>The intake temperatures the injection timing viewer offers (°C) and their AD values.</summary>
        public static IReadOnlyList<(int Celsius, int Ad)> Temperatures { get; } = [(-30, 230), (-10, 199), (20, 163), (40, 77), (60, 46), (80, 36)];

        /// <summary>Handle_temp_tables: linear between the steps; the step table ends at 255.</summary>
        public static int HandleTempTables(int ad, int[] tab, int[] steg)
        {
            if (tab.Length == 0 || steg.Length == 0) return 0;
            int i = 0;
            while (i < steg.Length - 1 && steg[i] != 255 && steg[i] < ad) i++;
            if (i == 0) return tab[0];
            i--;
            if (i + 1 >= tab.Length || steg[i + 1] == steg[i]) return tab[Math.Min(i, tab.Length - 1)];
            return tab[i + 1] > tab[i]
                ? tab[i] + (tab[i + 1] - tab[i]) * (ad - steg[i]) / (steg[i + 1] - steg[i])
                : tab[i] - (tab[i] - tab[i + 1]) * (ad - steg[i]) / (steg[i + 1] - steg[i]);
        }

        /// <summary>Handle_tables, "Trionic style": the mean of the x and the y interpolation at the lower-left cell (bytes wrap, as in the ECU's code).</summary>
        public static byte HandleTables(int y, byte x, byte[] matrix, int[] yAxis, int[] xAxis)
        {
            int yCount = yAxis.Length, xCount = xAxis.Length;
            int yi = 0, xi = 0;
            while (yi <= yCount - 1 && yAxis[yi] <= y) yi++;
            while (xi <= xCount - 1 && xAxis[xi] <= x) xi++;
            if (xi > 0) xi--;
            if (yi > 0) yi--;
            int At(int r, int c) => r * xCount + c < matrix.Length ? matrix[r * xCount + c] : 0;
            int tmp1 = At(yi, xi), tmp2 = yi < yCount - 1 ? At(yi + 1, xi) : tmp1, tmp3 = xi < xCount - 1 ? At(yi, xi + 1) : tmp1;
            byte vx, vy;
            if (xi < xCount - 1 && yi < yCount - 1)
            {
                vx = Interpolate(tmp3 - tmp1, xAxis[xi + 1] - xAxis[xi], x - xAxis[xi], tmp1);
                vy = Interpolate(tmp2 - tmp1, yAxis[yi + 1] - yAxis[yi], y - yAxis[yi], tmp1);
            }
            else
            {
                vx = Interpolate(tmp3 - tmp1, 0, x - xAxis[xi], tmp1);
                vy = Interpolate(tmp2 - tmp1, 0, y - yAxis[yi], tmp1);
            }
            return (byte)((vy + vx) / 2);
        }

        private static byte Interpolate(int delta, int span, int offset, int start) => span != 0 ? (byte)(delta * offset / span + start) : (byte)start;

        /// <summary>BoostRpmToInjectorDuration: the injection time in ms at a manifold pressure (kPa, 100 = 0 bar) and rpm on a fuel map.</summary>
        public double Milliseconds(int pressure, int rpm, byte[] map, int[] xAxis, int[] yAxis)
        {
            if (map.Length == 0 || xAxis.Length == 0 || yAxis.Length == 0) return 0;
            int ltf = HandleTempTables(IatAd, m_luftKompfak, m_lufttempSteg);
            int last = Math.Min(255, (ltf + 384) * pressure / 512);
            int cell = HandleTables(rpm, (byte)last, map, yAxis, xAxis);
            int grund = InjectorConstant * ((ltf + 384) * pressure) / 512;
            float t = grund * ((cell + 128f) / 256);
            int volt = Math.Clamp(BatteryVolt, 4, 14);
            if (14 - volt < m_battKorr.Length) t += m_battKorr[14 - volt];
            t = Math.Max(Math.Min(t, 32500), m_minTid);
            return t / 250;
        }

        /// <summary>The injector duty cycle in % at a time and rpm.</summary>
        public static double DutyCycle(double ms, int rpm) => rpm * ms / 1200;

        /// <summary>
        /// Show dyno graph (BuildGraph): per rpm of the boost request map's axis, the WOT column's pressure (× the MAP sensor factor),
        /// torque from the turbo's pressure-to-torque table, power and the injector duty cycle; the automatic map on automatic cars.
        /// </summary>
        public static List<(int Rpm, double Boost, double Torque, double Power, double DutyCycle)> Dyno(T5Binary bin)
        {
            var rows = new List<(int, double, double, double, double)>();
            Trionic5Properties p = bin.File.GetTrionicProperties();
            string mapName = p.AutomaticTransmission ? bin.Info.GetBoostRequestMapAUT() : bin.Info.GetBoostRequestMap();
            int[] rpm = bin.GetYaxisValues(bin.Info.GetBoostRequestMap());
            if (bin.Find(mapName) is not { } boostMap || bin.ReadSymbol(boostMap) is not { } tryck || bin.Find(bin.Info.GetInjectionMap()) is not { } fuel
                || bin.ReadSymbol(fuel) is not { } insp) return rows;
            var model = new T5InjectionModel(bin);
            int[] x = bin.GetXaxisValues(fuel.SmartVarname), y = bin.GetYaxisValues(fuel.SmartVarname);
            for (int i = 0; i < Math.Min(16, rpm.Length) && i * 8 + 7 < tryck.Length; i++)
            {
                int pressure = tryck[i * 8 + 7] * bin.SensorPercent / 100;
                double boost = (pressure - 100) / 100.0;
                double torque = new PressureToTorque().CalculateTorqueFromPressure(boost, p.TurboType);
                rows.Add((rpm[i], boost, torque, Realtime.Power(rpm[i], torque), DutyCycle(model.Milliseconds(pressure, rpm[i], insp, x, y), rpm[i])));
            }
            return rows;
        }
    }
}
