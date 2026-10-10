using System;
using System.Collections.Generic;
using System.Linq;

namespace CommonSuite
{
    /// <summary>What capped an airmass result cell (the legend's colours).</summary>
    public enum AirmassLimitType
    {
        None,
        TorqueLimiterEngine,
        TorqueLimiterEngineE85,
        TorqueLimiterGear,
        AirmassLimiter,
        TurboSpeedLimiter,
        FuelCutLimiter,
        OverBoostLimiter,
        AirTorqueCalibration,
        TorqueLimiterEngineE85Auto
    }

    /// <summary>The airmass result viewer's options (ctrlAirmassResult's Options group).</summary>
    public sealed record AirmassOptions
    {
        public bool Automatic { get; init; }
        public bool E85 { get; init; }
        /// <summary>T7: the car is a convertible (its gear limits); T8: the car is high output, 175/210 hp (the Tab1 limiters, else Tab2).</summary>
        public bool Variant { get; init; }
        public bool Overboost { get; init; }
        /// <summary>Torque from the suite's nominal torque map instead of airmass / 3.1.</summary>
        public bool TrionicTorque { get; init; }
        /// <summary>The 350 Nm (automatic, TCM) / 400 Nm (manual) firmware limit.</summary>
        public bool FirmwareLimited { get; init; } = true;
        public int AmbientKpa { get; init; } = 100;
        /// <summary>The gear list's index: T7 0 reverse, 1..5 first to fifth; T8 as ECMStat.ManualGear, 0 undefined, 1..6, 7 reverse.</summary>
        public int Gear { get; init; } = 5;
    }

    /// <summary>
    /// ctrlAirmassResult: what the ECU makes of the pedal request at every pedal position and rpm once the limiters had their say,
    /// and the torque, power, injector, lambda, EGT and fuel flow estimates. The suite reads its tables and applies its limiters;
    /// tables the bin lacks don't limit (both suites read them as 0 and clamped everything to 0, or showed nothing at all).
    /// </summary>
    public abstract class AirmassResult
    {
        protected AirmassResult(AirmassOptions options) => Options = options;

        public AirmassOptions Options { get; }

        /// <summary>The pedal map's rpm axis (columns) and pedal axis (rows, 0.1 %).</summary>
        public int[] Rpm { get; protected set; } = [];
        public int[] Pedal { get; protected set; } = [];

        /// <summary>[pedal row][rpm column], row 0 the lowest pedal position.</summary>
        public int[,] Airmass { get; private set; } = new int[0, 0];
        public AirmassLimitType[,] Limiter { get; private set; } = new AirmassLimitType[0, 0];

        public abstract bool CanE85 { get; }

        /// <summary>The option AirmassOptions.Variant stands for applies to this bin.</summary>
        public abstract bool CanVariant { get; }

        public abstract bool CanOverboost { get; }

        /// <summary>The maps a limiter's legend entry opens.</summary>
        public abstract string[] LimiterMaps(AirmassLimitType type);

        // the estimates' tables, read by the suite
        protected int[] Nominal, NominalX, NominalY;
        protected double InjectorConst;
        protected int[] BattCorr, BattCorrAxis, FuelMap, FuelAir, FuelRpm, EgtMap;

        /// <summary>The fuel map's 1.0: T7's BFuelCal.Map is in %, T8's TempEnrichFacMap in 1/128.</summary>
        protected double VeOne = 100;

        /// <summary>Below this airmass the engine runs closed loop and the EGT estimate isn't made richer; null: always richer (T8).</summary>
        protected virtual int? ClosedLoopLimit(int rpm) => null;

        /// <summary>Every cell's request ([pedal][rpm], rpm the fast index) through the suite's limiters.</summary>
        protected void Fill(int[] request, Func<int, int, (int airmass, AirmassLimitType limiter)> limit)
        {
            Airmass = new int[Pedal.Length, Rpm.Length];
            Limiter = new AirmassLimitType[Pedal.Length, Rpm.Length];
            for (int p = 0; p < Pedal.Length; p++)
                for (int r = 0; r < Rpm.Length; r++)
                {
                    int i = p * Rpm.Length + r;
                    (Airmass[p, r], Limiter[p, r]) = limit(Rpm[r], i < request.Length ? request[i] : 0);
                }
        }

        /// <summary>Big-endian 16-bit words, values above 32000 negative when signed; null for no data.</summary>
        public static int[] Words(byte[] d, bool signed = false)
        {
            if (d == null) return null;
            var w = new int[d.Length / 2];
            for (int i = 0; i < w.Length; i++)
            {
                int v = d[i * 2] << 8 | d[i * 2 + 1];
                w[i] = signed && v > 32000 ? v - 65536 : v;
            }
            return w;
        }

        /// <summary>
        /// GetInterpolatedTableValue: table [y][x]; per axis the last breakpoint the value is above (strictly) and the fraction
        /// to the next, clamped at both ends. Null when the table or an axis is missing.
        /// </summary>
        public static double? Interpolate(int[] table, int[] xaxis, int[] yaxis, double y, double x)
        {
            if (table == null || table.Length == 0 || xaxis == null || yaxis == null || xaxis.Length == 0 || yaxis.Length == 0) return null;
            (int xi, double xf) = Index(xaxis, x);
            (int yi, double yf) = Index(yaxis, y);
            int cols = xaxis.Length, rows = Math.Max(1, table.Length / cols);
            yi = Math.Min(yi, rows - 1);
            int V(int r, int c) => table[Math.Min(r * cols + c, table.Length - 1)];
            int x2 = Math.Min(xi + 1, cols - 1), y2 = Math.Min(yi + 1, rows - 1);
            double a = V(yi, xi) + xf * (V(yi, x2) - V(yi, xi));
            double b = V(y2, xi) + xf * (V(y2, x2) - V(y2, xi));
            return a + yf * (b - a);
        }

        private static (int, double) Index(int[] axis, double v)
        {
            int i = -1;
            for (int k = 0; k < axis.Length; k++)
                if (v > axis[k]) i = k;
            if (i < 0) return (0, 0);
            if (i >= axis.Length - 1) return (axis.Length - 1, 0);
            int span = axis[i + 1] - axis[i];
            return (i, span == 0 ? 0 : (v - axis[i]) / span);
        }

        /// <summary>A 1-D table on its axis, rounded like Convert.ToInt32.</summary>
        protected static int? Lookup(int[] table, int[] axis, double v) =>
            Interpolate(table, [0], axis, v, 0) is { } d ? Convert.ToInt32(d) : null;

        // ---- estimates ----

        private static double RpmCorrection(int rpm) => rpm >= 6000 ? 0.85 : rpm >= 5820 ? 0.94 : rpm >= 5440 ? 0.95 : rpm >= 5060 ? 0.99 : 1;

        /// <summary>AirmassToTorque: airmass / 3.1 (× 1.07 on E85) × the rpm correction, or the nominal torque map with the Trionic calculation.</summary>
        public int Torque(int airmass, int rpm)
        {
            if (Options.TrionicTorque && Interpolate(Nominal, NominalX, NominalY, rpm, airmass) is { } nominal) return Convert.ToInt32(nominal);
            double tq = airmass / 3.1;
            if (Options.E85) tq *= 1.07;
            return Convert.ToInt32(tq * RpmCorrection(rpm));
        }

        public static int Power(int torque, int rpm) => torque * rpm / 7121;
        public static int PowerKw(int torque, int rpm) => Convert.ToInt32(torque * rpm / 7121 * 0.73549875);
        public static int Lbft(int torque) => Convert.ToInt32(torque / 1.3558);

        private double? Ve(int airmass, int rpm) => Interpolate(FuelMap, FuelAir, FuelRpm, rpm, airmass) is { } ve && ve > 0 ? ve : null;

        /// <summary>CalculateInjectorDCusingPulseWidth: duty cycle %, uncapped; null without injector constant or fuel map.</summary>
        public double? InjectorDc(int airmass, int rpm)
        {
            if (InjectorConst <= 0 || Ve(airmass, rpm) is not { } ve) return null;
            double fuel = airmass / ((Options.E85 ? 9.84 : 14.65) * 1000);
            double pw = fuel / (InjectorConst / 60000) * ve / VeOne;
            double dc = Math.Round(pw * rpm / 1200);
            if (dc < 100 && Lookup(BattCorr, BattCorrAxis, 130) is { } batt) dc = Math.Round((pw + batt / 1000.0) * rpm / 1200);
            return dc;
        }

        /// <summary>CalculateTargetLambda: 1 / VE, richer by the injector overrun above 100 % duty.</summary>
        public double? TargetLambda(int airmass, int rpm)
        {
            if (Ve(airmass, rpm) is not { } ve) return null;
            double lambda = Convert.ToInt32(1 / (ve / VeOne) * 100);
            if (InjectorDc(airmass, rpm) is { } dc && dc > 100) lambda *= dc / 100;
            return lambda / 100;
        }

        public double? TargetAfr(int airmass, int rpm) => TargetLambda(airmass, rpm) * (Options.E85 ? 9.76 : 14.7);

        /// <summary>CalculateEstimateEGT: ExhaustCal.T_Lambda1Map, richer than λ 1 outside closed loop, 50 °C less on E85.</summary>
        public double? Egt(int airmass, int rpm)
        {
            if (Interpolate(EgtMap, FuelAir, FuelRpm, rpm, airmass) is not { } egt) return null;
            bool closedLoop = ClosedLoopLimit(rpm) is { } max && airmass < max;
            if (!closedLoop && Ve(airmass, rpm) is { } ve) egt *= 1 / (ve / VeOne);
            if (Options.E85 && egt > 50) egt -= 50;
            return egt;
        }

        /// <summary>CalculateFuelFlow: litres per hour.</summary>
        public double? FuelFlow(int airmass, int rpm)
        {
            if (TargetLambda(airmass, rpm) is not { } lambda || lambda == 0) return null;
            double gramsPerCombustion = airmass / ((Options.E85 ? 9.84 : 14.65) * 1000) / lambda;
            double perSecond = gramsPerCombustion * (rpm * 2 / 60);
            return perSecond * 3600 / (1000 * (Options.E85 ? 0.775 : 0.742));
        }
    }

    /// <summary>A compressor map image and where its axes sit, in pixels at the image's own size: (XOffset, YOffset) is 0 lb/min at pressure ratio 1.</summary>
    public sealed record Compressor(string Name, string Image, double XOffset, double YOffset, double PixelsPerLbMin, double PixelsPerPr);

    /// <summary>
    /// ctrlCompressorMap: the WOT airmass per rpm as operating points on a compressor map, for sea level (14.7 psi), high
    /// altitude (12.5) and high pressure (15.4). Both suites ship the same maps.
    /// </summary>
    public static class CompressorMap
    {
        public static readonly Compressor[] Compressors =
        [
            new("Garrett T1752", "GT17.jpg", 42, 539, 10.67, 166),
            new("Garrett T25 trim 55", "t25_55_saab.gif", 64, 865, 20, 396),
            new("Garrett T25 trim 60", "t25-60trim.gif", 60, 867, 17.28, 398),
            new("Mitsubishi TD04-15G", "td04-15g-cfm.gif", 66, 576, 10.45, 234.5),
            new("Mitsubishi TD04-16T", "td04h-16t-cfm.gif", 64, 573, 8.27, 233),
            new("Mitsubishi TD04-18T", "td04h-18t-cfm.gif", 65, 576, 8.27, 234),
            new("Mitsubishi TD04-19T", "td04h-19t-cfm.gif", 65, 576, 8.27, 234),
            new("Mitsubishi TD06-20G", "td06h-20g-cfm.gif", 58, 577, 8.30, 235),
            new("Garrett GT2871R", "gt2871r-48.jpg", 50, 595, 9.56, 276.5),
            new("Garrett GT28RS", "gt28rscompress.gif", 55, 460, 8, 211),
            new("Garrett GT3071R", "GT3071R86.jpg", 42, 556, 6.67, 171),
            new("Garrett GT3076R", "gt30rcompress.gif", 50, 463, 6.4, 158),
            new("Garrett GT40R", "gt40rcompress.gif", 54, 482, 5.31, 171),
            new("Holset HX40w", "hx40w.jpg", 35, 762, 5.03, 167),
            // T5Suite's own two
            new("BorgWarner S400SX3-71", "S400SX3-71.jpg", 45, 484, 6.713, 102),
            new("Garrett T25 54mm trim 60 (NG900, 9-3)", "T25_54mm_60trim_Map.jpg", 45, 622, 17.25, 258.5),
        ];

        /// <summary>Indexes into Compressors: the stock turbos the suites guess.</summary>
        public const int T1752 = 0, Td04_15G = 3, T25Trim60 = 2, Td04_19T = 6, Gt28Rs = 9, Gt3071R = 10, Hx40w = 13, S400 = 14, T25Ng900 = 15;

        // CalculateIntakeLoss: the intake's pressure loss in psi by rpm
        private static readonly (int below, double psi)[] IntakeLoss =
        [
            (880, .08), (1260, .10), (1640, .17), (2020, .28), (2400, .42), (2780, .50), (3160, .58), (3540, .65), (3920, .74), (4300, .82),
            (4680, .92), (5060, 1.03), (5440, 1.07), (5820, 1.10), (6000, 1.08),
        ];

        /// <summary>
        /// T5Suite's compressor map (ctrlCompressorMapEx): one curve from the WOT boost per rpm. Pressure ratio 1 + boost + the intake
        /// loss / 14.7; flow (14.5 + boost × 14.5) × displacement / 1728 × rpm / 2 × 29 / (10.73 × T °R) × VE, VE per rpm in %
        /// (0: 1 − rpm × 4 / 100000).
        /// </summary>
        public static List<(double lbmin, double pr)> PointsFromBoost(IReadOnlyList<int> rpm, IReadOnlyList<double> boostBar, double cubicInches,
            IReadOnlyList<double> vePercent, double tempC)
        {
            var points = new List<(double, double)>();
            double rankine = tempC * 1.8 + 32 + 460;
            for (int i = 0; i < rpm.Count && i < boostBar.Count; i++)
            {
                double pr = boostBar[i] + IntakeLoss.FirstOrDefault(l => rpm[i] < l.below, (below: 0, psi: 1.43)).psi / 14.7;
                double ve = i < vePercent.Count && vePercent[i] > 0 ? vePercent[i] / 100 : 1 - rpm[i] / 100000.0 * 4;
                double evf = cubicInches / 1728 * rpm[i] / 2;
                points.Add(((14.5 + pr * 14.5) * evf * 29 / (10.73 * rankine) * ve, 1 + pr));
            }
            return points;
        }

        public static readonly double[] AmbientPsi = [14.7, 12.5, 15.4];

        /// <summary>
        /// Operating points (lb/min, pressure ratio) per ambient pressure. Air flow = rpm × mg/c × 2 / 1000 g/min; the pressure
        /// ratio is the flow over what the engine swallows at ambient (VE, 1.2041 g/l), corrected to the intake temperature.
        /// </summary>
        public static List<(double lbmin, double pr)>[] Points(IReadOnlyList<int> rpm, IReadOnlyList<int> airmass, double litres, double vePercent, double tempC)
        {
            var curves = new List<(double, double)>[AmbientPsi.Length];
            for (int k = 0; k < curves.Length; k++) curves[k] = [];
            for (int i = 0; i < rpm.Count && i < airmass.Count; i++)
            {
                if (rpm[i] <= 0) continue;
                double ve = vePercent > 0 ? vePercent / 100 : 1 - rpm[i] / 100000.0 * 4;
                double swallowed = rpm[i] * ve * litres * 0.5 * 1.2041 / 453.59237;
                double flow = rpm[i] * airmass[i] * 2 / 1000.0 / 453.59237;
                double massRatio = flow / swallowed;
                for (int k = 0; k < AmbientPsi.Length; k++)
                    curves[k].Add((flow, massRatio * 14.7 / AmbientPsi[k] * (223 + tempC) / 223));
            }
            return curves;
        }
    }
}
