using System;
using System.Linq;
using CommonSuite;

namespace T8SuitePro
{
    /// <summary>
    /// T8Suite's ctrlAirmassResult: the pedal map is a torque request (0.1 Nm), turned into airmass through TrqMastCal.m_AirTorqMap,
    /// then the torque limiters (TrqLimCal, FFTrqCal, TMCCal, always on), the airmass limiter and the fuel cut; no turbo speed
    /// limiter. Options.Variant is "Car is high output (175/210 hp)": the Tab1 limiters, else Tab2. Tables come from the flash.
    /// </summary>
    public sealed class T8AirmassResult : AirmassResult
    {
        // 0.1 Nm: the TCM's limit, the ECU's own, none
        private const int TorqueLimitAuto = 3500, TorqueLimitManual = 4000, NoTorqueLimit = 10000;

        private readonly int[] m_airTorq, m_trqX, m_rpmY, m_maxAir, m_maxAirX, m_maxAirY, m_engine, m_e85, m_overboost, m_gear, m_auto;
        private readonly int? m_fuelCut;
        private readonly string m_engineName;

        public override bool CanE85 { get; }
        public override bool CanVariant => true;
        public override bool CanOverboost { get; }

        private string Tab => Options.Variant ? "1" : "2";

        public T8AirmassResult(T8Binary bin, AirmassOptions options) : base(options)
        {
            int[] W(string n, bool signed = false) => Words(Flash(bin, n), signed);
            CanE85 = bin.Has("FFTrqCal.FFTrq_MaxEngineTab1") || bin.Has("FFTrqCal.FFTrq_MaxEngineTab2");
            CanOverboost = !options.Automatic && Flash(bin, "TrqLimCal.EnableOverBoost") is { } ob && ob.Any(b => b != 0);
            Rpm = W("PedalMapCal.n_EngineMap") ?? [];
            Pedal = W("PedalMapCal.X_PedalMap") ?? [];
            m_airTorq = W("TrqMastCal.m_AirTorqMap");
            m_trqX = W("TrqMastCal.Trq_EngXSP");
            m_rpmY = W("TrqMastCal.n_EngineYSP");
            // CheckAgainstAirmassLimiters: automatic first, then E85, at 0° knock offset
            m_maxAirY = W("BstKnkCal.n_EngYSP");
            if (!options.Automatic && options.E85)
            {
                m_maxAir = W("FFAirCal.m_maxAirmass");
                m_maxAirX = W("FFAirCal.fi_offsetXSP", true);
            }
            else
            {
                m_maxAir = W(options.Automatic && bin.Has("BstKnkCal.MaxAirmassAu") ? "BstKnkCal.MaxAirmassAu" : "BstKnkCal.MaxAirmass");
                m_maxAirX = W(bin.Has("BstKnkCal.OffsetXSP") ? "BstKnkCal.OffsetXSP" : "BstKnkCal.fi_offsetXSP", true);
            }
            m_fuelCut = W("FCutCal.m_AirInletLimit") is { Length: > 0 } fc ? fc[0] : null;
            // the old Man / Aut engine tables, else the newer ones
            m_engineName = (options.Automatic ? "TrqLimCal.Trq_MaxEngineAutTab" : "TrqLimCal.Trq_MaxEngineManTab") + Tab;
            if (!bin.Has(m_engineName)) m_engineName = "TrqLimCal.Trq_MaxEngineTab" + Tab;
            m_engine = W(m_engineName);
            m_e85 = W("FFTrqCal.FFTrq_MaxEngineTab" + Tab);
            m_overboost = W("TrqLimCal.Trq_OverBoostTab");
            m_gear = W("TrqLimCal.Trq_ManGear");
            m_auto = W(options.Variant ? "TMCCal.Trq_MaxEngineTab" : "TMCCal.Trq_MaxEngineLowTab");

            // the estimates: the nominal map in whole Nm (truncated, as T8Suite), the fuel map in 1/128
            Nominal = W("TrqMastCal.Trq_NominalMap", true)?.Select(v => v / 10).ToArray();
            NominalX = W("TrqMastCal.m_AirXSP");
            NominalY = m_rpmY;
            InjectorConst = W("InjCorrCal.InjectorConst") is { Length: > 0 } ic ? ic[0] : 0;
            BattCorr = W("InjCorrCal.BattCorrTab");
            BattCorrAxis = W("InjCorrCal.BattCorrSP");
            FuelMap = Flash(bin, options.E85 ? "FFFuelCal.TempEnrichFacMAP" : "BFuelCal.TempEnrichFacMap")?.Select(b => (int)b).ToArray();
            VeOne = 128;
            FuelAir = W("BFuelCal.AirXSP");
            FuelRpm = W("BFuelCal.RpmYSP");
            EgtMap = W("ExhaustCal.T_Lambda1Map");
            Fill(W("PedalMapCal.Trq_RequestMap") ?? [], (rpm, torque) => Limit(rpm, TorqueToAirmass(torque, rpm)));
        }

        /// <summary>CheckAllTablesAvailable: the symbols the viewer needs.</summary>
        public static bool Available(T8Binary bin) =>
            new[] { "PedalMapCal.Trq_RequestMap", "TrqMastCal.m_AirTorqMap", "TrqMastCal.Trq_NominalMap", "BstKnkCal.MaxAirmass", "FCutCal.m_AirInletLimit", "TrqLimCal.Trq_ManGear" }
                .All(bin.Has)
            && (bin.Has("TrqLimCal.Trq_MaxEngineManTab1") || bin.Has("TrqLimCal.Trq_MaxEngineTab1"))
            && (bin.Has("TrqLimCal.Trq_MaxEngineAutTab1") || bin.Has("TrqLimCal.Trq_MaxEngineTab1"));

        /// <summary>The compressor map's first guess: the TD04-15G when the VIN says Mitsubishi TD04L-14T, else the T1752; always 2.0 litres.</summary>
        public static (int compressor, double litres) CompressorDefaults(string chassisId) =>
            (VINDecoder.DecodeVINNumber(chassisId).TurboModel == VINTurboModel.MitsubishiTD04L_14T ? CompressorMap.Td04_15G : CompressorMap.T1752, 2.0);

        // readdatafromfile: by SmartVarname, only from the flash (0x20000..0x100000); null when it isn't there
        private static byte[] Flash(T8Binary bin, string name) =>
            bin.Find(name) is { Length: > 0 } sh && sh.Flash_start_address >= 0x20000 && sh.Flash_start_address + sh.Length <= 0x100000
                ? bin.Read((int)sh.Flash_start_address, sh.Length) : null;

        // TorqueToAirmass: 0xFFFF and the like are negative (one off, as T8Suite: 0xFFFF is 0)
        private int TorqueToAirmass(int torque, int rpm) =>
            Interpolate(m_airTorq, m_trqX, m_rpmY, rpm, torque > 32000 ? torque - 65535 : torque) is { } air ? Convert.ToInt32(air) : 0;

        /// <summary>CalculateMaxAirmassforcell: the torque limiters, then the airmass limiter and the fuel cut.</summary>
        private (int, AirmassLimitType) Limit(int rpm, int request)
        {
            AirmassLimitType limiter = AirmassLimitType.None, air = AirmassLimitType.None;
            int restricted = TorqueLimit(rpm, request, out AirmassLimitType type);
            if (restricted < request) limiter = type;
            int torqueLimited = restricted;
            if (Interpolate(m_maxAir, m_maxAirX, m_maxAirY, rpm, 0) is { } max && Convert.ToInt32(max) < restricted)
            {
                restricted = Convert.ToInt32(max);
                air = AirmassLimitType.AirmassLimiter;
            }
            if (m_fuelCut is { } cut && cut < restricted)
            {
                restricted = cut;
                air = AirmassLimitType.FuelCutLimiter;
            }
            if (restricted < torqueLimited) limiter = air;
            return (restricted, limiter);
        }

        /// <summary>
        /// CheckAgainstTorqueLimiters in 0.1 Nm: the firmware limit, the E85 table or the engine table (overboost replacing it, even
        /// upwards), the automatic's TMCCal table or the manual's gear limit, then back to airmass through m_AirTorqMap. The type
        /// starts as Gear, as in T8Suite.
        /// </summary>
        private int TorqueLimit(int rpm, int request, out AirmassLimitType limiter)
        {
            limiter = AirmassLimitType.TorqueLimiterGear;
            int torque = Options.FirmwareLimited ? (Options.Automatic ? TorqueLimitAuto : TorqueLimitManual) : NoTorqueLimit;
            if (Options.E85)
            {
                if (Lookup(m_e85, m_rpmY, rpm) is { } e85 && torque > e85)
                {
                    torque = e85;
                    limiter = AirmassLimitType.TorqueLimiterEngineE85;
                }
            }
            else
            {
                int? engine = Lookup(m_engine, m_rpmY, rpm);
                int? overboost = Options.Overboost ? Lookup(m_overboost, m_rpmY, rpm) : null;
                if (overboost is { } ob && (torque > ob || torque < ob && engine is { } e && torque > e))
                {
                    torque = ob;
                    limiter = AirmassLimitType.OverBoostLimiter;
                }
                else if (engine is { } e2 && torque > e2)
                {
                    torque = e2;
                    limiter = AirmassLimitType.TorqueLimiterEngine;
                }
            }
            int? gear = Options.Automatic ? Lookup(m_auto, m_rpmY, rpm) : m_gear != null && Options.Gear < m_gear.Length ? m_gear[Options.Gear] : null;
            if (gear is { } g && torque > g)
            {
                torque = g;
                limiter = AirmassLimitType.TorqueLimiterGear;
            }
            return Interpolate(m_airTorq, m_trqX, m_rpmY, rpm, torque) is { } air && Convert.ToInt32(air) < request ? Convert.ToInt32(air) : request;
        }

        /// <summary>The legend's double-click: the table in use.</summary>
        public override string[] LimiterMaps(AirmassLimitType type) => type switch
        {
            AirmassLimitType.AirmassLimiter => [Options.Automatic ? "BstKnkCal.MaxAirmassAu" : Options.E85 ? "FFAirCal.m_maxAirmass" : "BstKnkCal.MaxAirmass"],
            AirmassLimitType.TorqueLimiterEngineE85 => ["FFTrqCal.FFTrq_MaxEngineTab" + Tab],
            AirmassLimitType.TorqueLimiterEngine => [m_engineName],
            AirmassLimitType.TorqueLimiterGear => [Options.Automatic ? Options.Variant ? "TMCCal.Trq_MaxEngineTab" : "TMCCal.Trq_MaxEngineLowTab" : "TrqLimCal.Trq_ManGear"],
            AirmassLimitType.FuelCutLimiter => ["FCutCal.m_AirInletLimit"],
            AirmassLimitType.OverBoostLimiter => ["TrqLimCal.Trq_OverBoostTab"],
            _ => [],
        };
    }
}
