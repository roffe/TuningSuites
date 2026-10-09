using System;
using System.Linq;

namespace T7
{
    /// <summary>The airmass result viewer's options (ctrlAirmassResult's Options group).</summary>
    public sealed record AirmassOptions
    {
        public bool Automatic { get; init; }
        public bool E85 { get; init; }
        public bool Convertible { get; init; }
        public bool Overboost { get; init; }
        /// <summary>Torque from TorqueCal.M_NominalMap instead of airmass / 3.1.</summary>
        public bool TrionicTorque { get; init; }
        /// <summary>The 350 Nm (automatic, TCM) / 400 Nm (manual) firmware limit.</summary>
        public bool FirmwareLimited { get; init; } = true;
        public int AmbientKpa { get; init; } = 100;
        /// <summary>0 reverse, 1..5 first to fifth.</summary>
        public int Gear { get; init; } = 5;
    }

    /// <summary>
    /// ctrlAirmassResult: what the ECU makes of the pedal request at every pedal position and rpm once the torque, airmass,
    /// turbo speed and fuel cut limiters had their say, and the torque, power, injector, lambda, EGT and fuel flow estimates.
    /// Tables the bin lacks don't limit (T7Suite read them as 0 and clamped everything to 0, or showed nothing at all).
    /// </summary>
    public sealed class AirmassResult
    {
        public AirmassOptions Options { get; }
        /// <summary>PedalMapCal.n_EngineMap (columns) and PedalMapCal.X_PedalMap (rows, 0.1 %).</summary>
        public int[] Rpm { get; }
        public int[] Pedal { get; }
        /// <summary>[pedal row][rpm column], row 0 the lowest pedal position.</summary>
        public int[,] Airmass { get; }
        public AirmassLimitType[,] Limiter { get; }

        private readonly T7Binary m_bin;
        private readonly Tables t;

        public bool CanE85 => t.EngMaxE85 != null;
        public bool CanConvertible => t.CabGearLim != null;
        public bool CanOverboost { get; }

        private sealed class Tables
        {
            public int[] Request, PedalRpm, PedalAxis;
            public int[] AirTorq, EngX, EngY;
            public int[] MaxAir, MaxAirAu, OffsetX, KnkY;
            public int[] TurboSpeed, TurboP, TurboSpeed2, TurboN;
            public int[] Nominal, NominalX;
            public int? FuelCut;
            public int[] EngMax, EngMaxAut, EngMaxE85, EngMaxE85Aut, OverBoost, CabGearLim, ManGearLim, Gear5, Gear5Axis, Gear1, Gear1Axis;
            public bool TorqueLimitEnabled;
            public double InjectorConst;
            public int[] BattCorr, BattCorrAxis, FuelMap, FuelAir, FuelRpm, MaxLoad, MaxLoadRpm, Egt;
        }

        public AirmassResult(T7Binary bin, AirmassOptions options)
        {
            m_bin = bin;
            Options = options;
            t = Read(bin, options);
            CanOverboost = Bytes("TorqueCal.EnableOverBoost") is { } ob && ob.Any(b => b != 0);
            Rpm = t.PedalRpm;
            Pedal = t.PedalAxis;
            Airmass = new int[Pedal.Length, Rpm.Length];
            Limiter = new AirmassLimitType[Pedal.Length, Rpm.Length];
            for (int p = 0; p < Pedal.Length; p++)
                for (int r = 0; r < Rpm.Length; r++)
                {
                    int i = p * Rpm.Length + r;
                    int request = i < t.Request.Length ? t.Request[i] : 0;
                    Airmass[p, r] = Limit(Rpm[r], request, out AirmassLimitType limiter);
                    Limiter[p, r] = limiter;
                }
        }

        /// <summary>The symbols the viewer needs (CheckAllTablesAvailable).</summary>
        public static bool Available(T7Binary bin) =>
            new[]
            {
                "PedalMapCal.m_RequestMap", "TorqueCal.m_AirTorqMap", "TorqueCal.M_NominalMap", "BstKnkCal.MaxAirmass", "TorqueCal.M_EngMaxTab",
                "TorqueCal.M_EngMaxAutTab", "TorqueCal.m_AirXSP", "TorqueCal.n_EngYSP", "TorqueCal.M_EngXSP", "BstKnkCal.OffsetXSP",
                "BstKnkCal.n_EngYSP", "PedalMapCal.n_EngineMap", "PedalMapCal.X_PedalMap",
            }.All(n => bin.FindAny(n) != null);

        private byte[] Bytes(string name) => m_bin.FindAny(name) is { } sh ? m_bin.ReadSymbol(sh) : null;

        private static int[] U16(byte[] d, bool signed = false) =>
            d == null ? null : Enumerable.Range(0, d.Length / 2).Select(i => d[i * 2] << 8 | d[i * 2 + 1]).Select(v => signed && v > 32000 ? v - 65536 : v).ToArray();

        private static Tables Read(T7Binary bin, AirmassOptions o)
        {
            byte[] B(string n) => bin.FindAny(n) is { } sh ? bin.ReadSymbol(sh) : null;
            int[] W(string n, bool signed = false) => U16(B(n), signed);
            var t = new Tables
            {
                Request = W("PedalMapCal.m_RequestMap") ?? [],
                PedalRpm = W("PedalMapCal.n_EngineMap") ?? [],
                PedalAxis = W("PedalMapCal.X_PedalMap") ?? [],
                AirTorq = W("TorqueCal.m_AirTorqMap"), EngX = W("TorqueCal.M_EngXSP"), EngY = W("TorqueCal.n_EngYSP"),
                MaxAir = W("BstKnkCal.MaxAirmass"), MaxAirAu = W("BstKnkCal.MaxAirmassAu"), OffsetX = W("BstKnkCal.OffsetXSP", true), KnkY = W("BstKnkCal.n_EngYSP"),
                TurboSpeed = W("LimEngCal.TurboSpeedTab"), TurboP = W("LimEngCal.p_AirSP"), TurboSpeed2 = W("LimEngCal.TurboSpeedTab2"), TurboN = W("LimEngCal.n_EngSP"),
                Nominal = W("TorqueCal.M_NominalMap", true), NominalX = W("TorqueCal.m_AirXSP"),
                FuelCut = W("FCutCal.m_AirInletLimit") is { Length: > 0 } fc ? fc[0] : null,
                EngMax = W("TorqueCal.M_EngMaxTab"), EngMaxAut = W("TorqueCal.M_EngMaxAutTab"), EngMaxE85 = W("TorqueCal.M_EngMaxE85Tab"),
                EngMaxE85Aut = W("TorqueCal.M_EngMaxE85TabAut"), OverBoost = W("TorqueCal.M_OverBoostTab"),
                CabGearLim = W("TorqueCal.M_CabGearLim"), ManGearLim = W("TorqueCal.M_ManGearLim"),
                Gear5 = W("TorqueCal.M_5GearLimTab"), Gear5Axis = W("TorqueCal.n_Eng5GearSP"), Gear1 = W("TorqueCal.M_1GearTab"), Gear1Axis = W("TorqueCal.n_Eng1GearSP"),
                // TorqueCal.ST_Loop = 0 switches the torque limiters off
                TorqueLimitEnabled = !(B("TorqueCal.ST_Loop") is { Length: 1 } loop && loop[0] == 0),
                InjectorConst = W("InjCorrCal.InjectorConst") is { Length: > 0 } ic ? ic[0] : 0,
                BattCorr = W("InjCorrCal.BattCorrTab"), BattCorrAxis = W("InjCorrCal.BattCorrSP"),
                FuelMap = B(o.E85 ? "BFuelCal.E85Map" : "BFuelCal.Map")?.Select(b => (int)b).ToArray(), FuelAir = W("BFuelCal.AirXSP"), FuelRpm = W("BFuelCal.RpmYSP"),
                MaxLoad = W(o.E85 ? "LambdaCal.MaxLoadE85Tab" : "LambdaCal.MaxLoadNormTab"), MaxLoadRpm = W("LambdaCal.RpmSp"),
                Egt = W("ExhaustCal.T_Lambda1Map"),
            };
            return t;
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

        // a 1-D table on its axis, rounded like Convert.ToInt32
        private static int? Lookup(int[] table, int[] axis, double v) =>
            Interpolate(table, [0], axis, v, 0) is { } d ? Convert.ToInt32(d) : null;

        private int? GearValue(int[] table) => table != null && Options.Gear < table.Length ? table[Options.Gear] : null;

        /// <summary>CalculateMaxAirmassforcell.</summary>
        private int Limit(int rpm, int request, out AirmassLimitType limiter)
        {
            limiter = AirmassLimitType.None;
            int restricted = request;
            if (t.TorqueLimitEnabled)
            {
                restricted = TorqueLimit(rpm, request, out AirmassLimitType type);
                if (restricted < request) limiter = type;
            }
            int torqueLimited = restricted;
            AirmassLimitType air = AirmassLimitType.None;
            void Apply(int? limit, AirmassLimitType type)
            {
                if (limit is { } l && l < restricted)
                {
                    restricted = l;
                    air = type;
                }
            }
            // at the column where the knock offset is 0
            if (Interpolate(Options.Automatic ? t.MaxAirAu : t.MaxAir, t.OffsetX, t.KnkY, rpm, 0) is { } max) Apply(Convert.ToInt32(max), AirmassLimitType.AirmassLimiter);
            if (Lookup(t.TurboSpeed, t.TurboP, Options.AmbientKpa * 10) is { } ts && Lookup(t.TurboSpeed2, t.TurboN, rpm) is { } factor)
                Apply(ts * factor / 1000, AirmassLimitType.TurboSpeedLimiter);
            Apply(t.FuelCut, AirmassLimitType.FuelCutLimiter);
            if (restricted < torqueLimited) limiter = air;
            return restricted;
        }

        /// <summary>
        /// CheckAgainstTorqueLimiters: the firmware limit, the engine table for the fuel and gearbox (overboost replaces the
        /// petrol table, even upwards), the gear limits for manuals, then back to airmass through m_AirTorqMap. The type starts
        /// as Gear, so a cut by m_AirTorqMap alone (the firmware limit) shows as the gear limiter, as in T7Suite.
        /// </summary>
        private int TorqueLimit(int rpm, int request, out AirmassLimitType limiter)
        {
            AirmassLimitType type = AirmassLimitType.TorqueLimiterGear;
            int torque = Options.FirmwareLimited ? (Options.Automatic ? 350 : 400) : 1000;
            void Lower(int? limit, AirmassLimitType t2)
            {
                if (limit is { } l && l < torque)
                {
                    torque = l;
                    type = t2;
                }
            }
            if (Options.E85 && Options.Automatic && t.EngMaxE85Aut != null) Lower(Lookup(t.EngMaxE85Aut, t.EngY, rpm), AirmassLimitType.TorqueLimiterEngineE85Auto);
            else if (Options.E85) Lower(Lookup(t.EngMaxE85, t.EngY, rpm), AirmassLimitType.TorqueLimiterEngineE85);
            else if (Options.Automatic) Lower(Lookup(t.EngMaxAut, t.EngY, rpm), AirmassLimitType.TorqueLimiterEngine);
            else
            {
                int? petrol = Lookup(t.EngMax, t.EngY, rpm);
                int? overboost = Options.Overboost ? Lookup(t.OverBoost, t.EngY, rpm) : null;
                if (overboost is { } ob && (torque > ob || torque < ob && petrol is { } p && torque > p))
                {
                    torque = ob;
                    type = AirmassLimitType.OverBoostLimiter;
                }
                else Lower(petrol, AirmassLimitType.TorqueLimiterEngine);
            }
            if (!Options.Automatic)
            {
                if (Options.Convertible) Lower(GearValue(t.CabGearLim), AirmassLimitType.TorqueLimiterGear);
                Lower(GearValue(t.ManGearLim), AirmassLimitType.TorqueLimiterGear);
                if (Options.Gear == 1 && t.Gear1 != null) Lower(Lookup(t.Gear1, t.Gear1Axis, rpm), AirmassLimitType.TorqueLimiterGear);
                else if (Options.Gear == 5) Lower(Lookup(t.Gear5, t.Gear5Axis, rpm), AirmassLimitType.TorqueLimiterGear);
            }
            limiter = type;
            if (Interpolate(t.AirTorq, t.EngX, t.EngY, rpm, torque) is { } air && Convert.ToInt32(air) < request) return Convert.ToInt32(air);
            return request;
        }

        // ---- estimates ----

        private static double RpmCorrection(int rpm) => rpm >= 6000 ? 0.85 : rpm >= 5820 ? 0.94 : rpm >= 5440 ? 0.95 : rpm >= 5060 ? 0.99 : 1;

        /// <summary>AirmassToTorque: airmass / 3.1 (× 1.07 on E85) × the rpm correction, or M_NominalMap with the Trionic calculation.</summary>
        public int Torque(int airmass, int rpm)
        {
            if (Options.TrionicTorque && Interpolate(t.Nominal, t.NominalX, t.EngY, rpm, airmass) is { } nominal) return Convert.ToInt32(nominal);
            double tq = airmass / 3.1;
            if (Options.E85) tq *= 1.07;
            return Convert.ToInt32(tq * RpmCorrection(rpm));
        }

        public static int Power(int torque, int rpm) => torque * rpm / 7121;
        public static int PowerKw(int torque, int rpm) => Convert.ToInt32(torque * rpm / 7121 * 0.73549875);
        public static int Lbft(int torque) => Convert.ToInt32(torque / 1.3558);

        private double? Ve(int airmass, int rpm) => Interpolate(t.FuelMap, t.FuelAir, t.FuelRpm, rpm, airmass) is { } ve && ve > 0 ? ve : null;

        /// <summary>CalculateInjectorDCusingPulseWidth: duty cycle %, uncapped; null without injector constant or fuel map.</summary>
        public double? InjectorDc(int airmass, int rpm)
        {
            if (t.InjectorConst <= 0 || Ve(airmass, rpm) is not { } ve) return null;
            double fuel = airmass / ((Options.E85 ? 9.84 : 14.65) * 1000);
            double pw = fuel / (t.InjectorConst / 60000) * ve / 100;
            double dc = Math.Round(pw * rpm / 1200);
            if (dc < 100 && Lookup(t.BattCorr, t.BattCorrAxis, 130) is { } batt) dc = Math.Round((pw + batt / 1000.0) * rpm / 1200);
            return dc;
        }

        /// <summary>CalculateTargetLambda: 1 / VE, richer by the injector overrun above 100 % duty.</summary>
        public double? TargetLambda(int airmass, int rpm)
        {
            if (Ve(airmass, rpm) is not { } ve) return null;
            double lambda = Convert.ToInt32(1 / (ve / 100) * 100);
            if (InjectorDc(airmass, rpm) is { } dc && dc > 100) lambda *= dc / 100;
            return lambda / 100;
        }

        public double? TargetAfr(int airmass, int rpm) => TargetLambda(airmass, rpm) * (Options.E85 ? 9.76 : 14.7);

        /// <summary>CalculateEstimateEGT: ExhaustCal.T_Lambda1Map, richer than λ 1 above the closed loop limit.</summary>
        public double? Egt(int airmass, int rpm)
        {
            if (Interpolate(t.Egt, t.FuelAir, t.FuelRpm, rpm, airmass) is not { } egt) return null;
            bool closedLoop = Lookup(t.MaxLoad, t.MaxLoadRpm, rpm) is { } max && airmass < max;
            if (!closedLoop && Ve(airmass, rpm) is { } ve) egt *= 1 / (ve / 100);
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

        /// <summary>The maps a limiter's legend entry opens.</summary>
        public static string[] LimiterMaps(AirmassLimitType type, AirmassOptions o) => type switch
        {
            AirmassLimitType.TorqueLimiterEngine => [o.Automatic ? "TorqueCal.M_EngMaxAutTab" : "TorqueCal.M_EngMaxTab"],
            AirmassLimitType.AirmassLimiter => [o.Automatic ? "BstKnkCal.MaxAirmassAu" : "BstKnkCal.MaxAirmass"],
            AirmassLimitType.TurboSpeedLimiter => ["LimEngCal.TurboSpeedTab", "LimEngCal.TurboSpeedTab2"],
            AirmassLimitType.TorqueLimiterEngineE85 => ["TorqueCal.M_EngMaxE85Tab"],
            AirmassLimitType.TorqueLimiterEngineE85Auto => ["TorqueCal.M_EngMaxE85TabAut"],
            AirmassLimitType.TorqueLimiterGear => [o.Gear == 1 ? "TorqueCal.M_1GearTab" : o.Gear == 5 ? "TorqueCal.M_5GearLimTab" : o.Convertible ? "TorqueCal.M_CabGearLim" : "TorqueCal.M_ManGearLim"],
            AirmassLimitType.FuelCutLimiter => ["FCutCal.m_AirInletLimit"],
            AirmassLimitType.OverBoostLimiter => ["TorqueCal.M_OverBoostTab"],
            _ => [],
        };
    }
}
