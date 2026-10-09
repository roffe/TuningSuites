using System;
using System.Linq;
using CommonSuite;

namespace T7
{
    /// <summary>
    /// T7Suite's ctrlAirmassResult: the pedal map's airmass request through the torque limiters (TorqueCal), the airmass,
    /// turbo speed and fuel cut limiters. Options.Variant is "Car is a convertible".
    /// </summary>
    public sealed class T7AirmassResult : AirmassResult
    {
        private readonly Tables t;

        public override bool CanE85 => t.EngMaxE85 != null;
        public override bool CanVariant => t.CabGearLim != null;
        public override bool CanOverboost { get; }

        private bool Convertible => Options.Variant;

        private sealed class Tables
        {
            public int[] Request, PedalRpm, PedalAxis;
            public int[] AirTorq, EngX, EngY;
            public int[] MaxAir, MaxAirAu, OffsetX, KnkY;
            public int[] TurboSpeed, TurboP, TurboSpeed2, TurboN;
            public int? FuelCut;
            public int[] EngMax, EngMaxAut, EngMaxE85, EngMaxE85Aut, OverBoost, CabGearLim, ManGearLim, Gear5, Gear5Axis, Gear1, Gear1Axis;
            public bool TorqueLimitEnabled;
            public int[] MaxLoad, MaxLoadRpm;
        }

        public T7AirmassResult(T7Binary bin, AirmassOptions options) : base(options)
        {
            byte[] B(string n) => bin.FindAny(n) is { } sh ? bin.ReadSymbol(sh) : null;
            int[] W(string n, bool signed = false) => Words(B(n), signed);
            t = new Tables
            {
                Request = W("PedalMapCal.m_RequestMap") ?? [],
                PedalRpm = W("PedalMapCal.n_EngineMap") ?? [],
                PedalAxis = W("PedalMapCal.X_PedalMap") ?? [],
                AirTorq = W("TorqueCal.m_AirTorqMap"), EngX = W("TorqueCal.M_EngXSP"), EngY = W("TorqueCal.n_EngYSP"),
                MaxAir = W("BstKnkCal.MaxAirmass"), MaxAirAu = W("BstKnkCal.MaxAirmassAu"), OffsetX = W("BstKnkCal.OffsetXSP", true), KnkY = W("BstKnkCal.n_EngYSP"),
                TurboSpeed = W("LimEngCal.TurboSpeedTab"), TurboP = W("LimEngCal.p_AirSP"), TurboSpeed2 = W("LimEngCal.TurboSpeedTab2"), TurboN = W("LimEngCal.n_EngSP"),
                FuelCut = W("FCutCal.m_AirInletLimit") is { Length: > 0 } fc ? fc[0] : null,
                EngMax = W("TorqueCal.M_EngMaxTab"), EngMaxAut = W("TorqueCal.M_EngMaxAutTab"), EngMaxE85 = W("TorqueCal.M_EngMaxE85Tab"),
                EngMaxE85Aut = W("TorqueCal.M_EngMaxE85TabAut"), OverBoost = W("TorqueCal.M_OverBoostTab"),
                CabGearLim = W("TorqueCal.M_CabGearLim"), ManGearLim = W("TorqueCal.M_ManGearLim"),
                Gear5 = W("TorqueCal.M_5GearLimTab"), Gear5Axis = W("TorqueCal.n_Eng5GearSP"), Gear1 = W("TorqueCal.M_1GearTab"), Gear1Axis = W("TorqueCal.n_Eng1GearSP"),
                // TorqueCal.ST_Loop = 0 switches the torque limiters off
                TorqueLimitEnabled = !(B("TorqueCal.ST_Loop") is { Length: 1 } loop && loop[0] == 0),
                MaxLoad = W(options.E85 ? "LambdaCal.MaxLoadE85Tab" : "LambdaCal.MaxLoadNormTab"), MaxLoadRpm = W("LambdaCal.RpmSp"),
            };
            Nominal = W("TorqueCal.M_NominalMap", true);
            NominalX = W("TorqueCal.m_AirXSP");
            NominalY = t.EngY;
            InjectorConst = W("InjCorrCal.InjectorConst") is { Length: > 0 } ic ? ic[0] : 0;
            BattCorr = W("InjCorrCal.BattCorrTab");
            BattCorrAxis = W("InjCorrCal.BattCorrSP");
            FuelMap = B(options.E85 ? "BFuelCal.E85Map" : "BFuelCal.Map")?.Select(b => (int)b).ToArray();
            FuelAir = W("BFuelCal.AirXSP");
            FuelRpm = W("BFuelCal.RpmYSP");
            EgtMap = W("ExhaustCal.T_Lambda1Map");
            CanOverboost = B("TorqueCal.EnableOverBoost") is { } ob && ob.Any(b => b != 0);
            Rpm = t.PedalRpm;
            Pedal = t.PedalAxis;
            Fill(t.Request, (rpm, request) => (Limit(rpm, request, out AirmassLimitType limiter), limiter));
        }

        /// <summary>The symbols the viewer needs (CheckAllTablesAvailable).</summary>
        public static bool Available(T7Binary bin) =>
            new[]
            {
                "PedalMapCal.m_RequestMap", "TorqueCal.m_AirTorqMap", "TorqueCal.M_NominalMap", "BstKnkCal.MaxAirmass", "TorqueCal.M_EngMaxTab",
                "TorqueCal.M_EngMaxAutTab", "TorqueCal.m_AirXSP", "TorqueCal.n_EngYSP", "TorqueCal.M_EngXSP", "BstKnkCal.OffsetXSP",
                "BstKnkCal.n_EngYSP", "PedalMapCal.n_EngineMap", "PedalMapCal.X_PedalMap",
            }.All(n => bin.FindAny(n) != null);

        /// <summary>The compressor map's first guess: the TD04-15G on Aero part numbers, else the T1752; 2.3 litres unless B204 / B205.</summary>
        public static (int compressor, double litres) CompressorDefaults(string partNumber)
        {
            ECUInformation ecu = new PartNumberConverter().GetECUInfo(partNumber.Trim(), "");
            return (ecu.Isaero ? CompressorMap.Td04_15G : CompressorMap.T1752, ecu.Is2point3liter ? 2.3 : 2.0);
        }

        // the closed loop limit (LambdaCal.MaxLoad) keeps the EGT estimate at λ 1
        protected override int? ClosedLoopLimit(int rpm) => Lookup(t.MaxLoad, t.MaxLoadRpm, rpm);

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
                if (Convertible) Lower(GearValue(t.CabGearLim), AirmassLimitType.TorqueLimiterGear);
                Lower(GearValue(t.ManGearLim), AirmassLimitType.TorqueLimiterGear);
                if (Options.Gear == 1 && t.Gear1 != null) Lower(Lookup(t.Gear1, t.Gear1Axis, rpm), AirmassLimitType.TorqueLimiterGear);
                else if (Options.Gear == 5) Lower(Lookup(t.Gear5, t.Gear5Axis, rpm), AirmassLimitType.TorqueLimiterGear);
            }
            limiter = type;
            if (Interpolate(t.AirTorq, t.EngX, t.EngY, rpm, torque) is { } air && Convert.ToInt32(air) < request) return Convert.ToInt32(air);
            return request;
        }

        public override string[] LimiterMaps(AirmassLimitType type) => type switch
        {
            AirmassLimitType.TorqueLimiterEngine => [Options.Automatic ? "TorqueCal.M_EngMaxAutTab" : "TorqueCal.M_EngMaxTab"],
            AirmassLimitType.AirmassLimiter => [Options.Automatic ? "BstKnkCal.MaxAirmassAu" : "BstKnkCal.MaxAirmass"],
            AirmassLimitType.TurboSpeedLimiter => ["LimEngCal.TurboSpeedTab", "LimEngCal.TurboSpeedTab2"],
            AirmassLimitType.TorqueLimiterEngineE85 => ["TorqueCal.M_EngMaxE85Tab"],
            AirmassLimitType.TorqueLimiterEngineE85Auto => ["TorqueCal.M_EngMaxE85TabAut"],
            AirmassLimitType.TorqueLimiterGear => [Options.Gear == 1 ? "TorqueCal.M_1GearTab" : Options.Gear == 5 ? "TorqueCal.M_5GearLimTab" : Convertible ? "TorqueCal.M_CabGearLim" : "TorqueCal.M_ManGearLim"],
            AirmassLimitType.FuelCutLimiter => ["FCutCal.m_AirInletLimit"],
            AirmassLimitType.OverBoostLimiter => ["TorqueCal.M_OverBoostTab"],
            _ => [],
        };
    }
}
