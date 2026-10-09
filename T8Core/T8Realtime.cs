using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommonSuite;
using TrionicCANLib.API;

namespace T8SuitePro
{
    /// <summary>T8Suite's realtime table: Form1.FillRealtimeTable, its signed list, per-cylinder counters, status texts and maps.</summary>
    public sealed class T8Realtime : RealtimeRules
    {
        public static T8Realtime Rules { get; } = new();

        public override IReadOnlySet<string> Signed { get; } = new HashSet<string>
        {
            "ActualIn.T_Engine", "ActualIn.T_AirInlet", "Out.fi_Ignition", "Out.M_EngTrqAct", "ECMStat.P_Engine", "IgnMastProt.fi_Offset",
            "Lambda.LambdaInt", "MAF.m_AirInlet", "AdpFuelProt.MulFuelAdapt", "ECMStat.p_Diff", "BoostProt.PFac", "BoostProt.IFac",
            "BoostProt.LoadDiff", "IgnKnk.fi_MeanKnock", "Ign.fi_OtherOff", "IgnJerkProt.fi_Offset",
        };

        public override IReadOnlyDictionary<string, (string Prefix, int Size)> PerCylinder { get; } = new Dictionary<string, (string, int)>
        {
            ["KnkDet.KnockCyl"] = ("KnockCyl", 1),
            ["KnkDetAdap.KnkCntCyl"] = ("KnkCntCyl", 2),
            ["MisfAdap.N_MisfCountCyl"] = ("MisfCyl", 2),
        };

        public override DashboardSymbols Symbols { get; } = new("Out.M_EngTrqAct", "IgnMastProt.fi_Offset", "AirMassMast.m_Request", "Out.X_AccPos");

        public override string LogExtension => "t8l";

        public override string LogFilesName => "Trionic 8 logfiles";

        public override string LayoutExtension => "t8rtl";

        public override string LogWorksWidebandSymbol(AppSettings settings) => "";

        /// <summary>T7Suite's rows with T8's names, the battery voltage first and no fuel consumption ("TODO: Fix for Trionic 8").</summary>
        public override List<RealtimeSymbol> Dashboard(SuiteBinary bin, AppSettings settings)
        {
            var rows = new List<RealtimeSymbol>
            {
                Row(bin, "ActualIn.U_Battery", "Battery voltage", 0, 0.1, 0, 16),
                Row(bin, "ActualIn.n_Engine", "Engine speed", 0, 1, 0, 8000),
                Row(bin, "In.v_Vehicle", "Vehicle speed", 0, 0.1, 0, 300, 3),
                Row(bin, "Out.X_AccPos", "TPS %", 0, 0.1, 0, 100),
                Row(bin, "ActualIn.T_Engine", "Engine temperature", 0, 1, -20, 120, 5),
                Row(bin, "ActualIn.T_AirInlet", "Intake air temperature", 0, 1, -20, 120, 3),
                Row(bin, "ECMStat.ST_ActiveAirDem", "Active air demand map", 0, 1, 0, 255),
                Row(bin, "Lambda.Status", "Lambda status", 0, 1, 0, 255),
                Row(bin, "FCut.CutStatus", "Fuelcut status", 0, 1, 0, 255),
                Row(bin, "IgnMastProt.fi_Offset", "Ignition offset", 0, 0.1, -20, 20),
                Row(bin, "AirMassMast.m_Request", "Requested airmass", 0, 1, 0, 600),
                Row(bin, "Out.M_EngTrqAct", "Calculated torque", 0, 1, 0, 600),
                Row(bin, "In.p_AirInlet", "Boost", -1, 0.001, -1, 3),
                Row(bin, "Out.PWM_BoostCntrl", "Duty cycle BCV", 0, 0.1, 0, 100),
                Row(bin, "Out.fi_Ignition", "Ignition advance", 0, 0.1, -10, 50),
                Row(bin, "MAF.m_AirInlet", "Actual airmass", 0, 1, 0, 1600),
            };
            if (Realtime.HasEgtCalculation(bin)) rows.Add(Row(bin, "Exhaust.T_Calc", "Calculated EGT temperature", 0, 1, 0, 1200, 2));
            if (!settings.UseWidebandLambda) rows.Add(Row(bin, "Lambda.LambdaInt", "Lambda value (nbO2)", 1, 0.0001, 0, 2));
            else rows.Add(Row(bin, settings.WideBandSymbol, "Lambda value (wbO2)", 0, settings.WideBandSymbol == "DisplProt.AD_Scanner" ? 1 : 0.1, 10, 20));
            return rows.Where(r => r != null).ToList();
        }

        public override RealtimeSymbol FromSymbol(SymbolHelper sh)
        {
            string name = sh.SmartVarname;
            var (min, max, correction) = name switch
            {
                "ActualIn.v_Vehicle2" or "In.v_Vehicle" => (0d, 255d, 0.1),
                "FFTrqProt.Trq_MaxEngineBefComp" or "FFTrqProt.Trq_MaxEngine" => (0, 65535, 0.1),
                _ => (0, sh.Length == 1 ? 255 : 65535, 1),
            };
            return UserRow(sh, name, min, max, correction);
        }

        // UpdateOpenViewers' maps; T8Suite misspelt the boost map (AirCrtlCal), so it never showed its cell
        public override IReadOnlyList<CellRule> CellRules { get; } =
        [
            new(["IgnAbsCal.fi_NormalMAP", "IgnAbsCal.fi_lowOctanMAP", "IgnAbsCal.fi_highOctanMAP"], "IgnAbsCal.m_AirNormXSP", CellInput.Airmass, 1, "IgnAbsCal.n_EngNormYSP", CellInput.Rpm, 1),
            new(["KnkFuelCal.fi_MaxOffsetMap"], "KnkFuelCal.m_AirXSP", CellInput.Airmass, 1, "BstKnkCal.n_EngYSP", CellInput.Rpm, 1),
            new(["IgnKnkCal.IndexMap"], "IgnKnkCal.m_AirXSP", CellInput.Airmass, 1, "IgnKnkCal.n_EngYSP", CellInput.Rpm, 1),
            new(["BFuelCal.LambdaOneFacMap"], "BFuelCal.AirXSP", CellInput.Airmass, 1, "BFuelCal.RpmYSP", CellInput.Rpm, 1),
            new(["PedalMapCal.Trq_RequestMap"], "PedalMapCal.n_EngineMap", CellInput.Rpm, 1, "PedalMapCal.X_PedalMap", CellInput.Tps, 0.1),
            new(["TrqMastCal.m_AirTorqMap"], "TrqMastCal.Trq_EngXSP", CellInput.Torque, 1, "TrqMastCal.n_EngineYSP", CellInput.Rpm, 1),
            new(["TrqMastCal.Trq_NominalMap"], "TrqMastCal.m_AirXSP", CellInput.Airmass, 1, "TrqMastCal.n_EngineYSP", CellInput.Rpm, 1),
            new(["AirCtrlCal.RegMap"], "AirCtrlCal.SetLoadXSP", CellInput.Airmass, 1, "AirCtrlCal.n_EngYSP", CellInput.Rpm, 1),
            new(["BstKnkCal.MaxAirmass"], "BstKnkCal.OffsetXSP|BstKnkCal.fi_offsetXSP", CellInput.IgnitionOffset, 0.1, "BstKnkCal.n_EngYSP", CellInput.Rpm, 1),
        ];

        private static readonly Dictionary<int, string> s_fuelcut = new()
        {
            [0] = "No fuelcut",
            [1] = "Ignition key turned off",
            [2] = "Accelerator pedal pressed during start",
            [3] = "RPM limiter (engine speed guard)",
            [4] = "Throttle block adaption active 1st time",
            [5] = "Engine position lost",
            [6] = "Airmass limit (pressure guard)",
            [7] = "Immobilizer code incorrect",
            [8] = "Starter control relay circuit short to ground",
            [9] = "Starter control relay circuit short to ground",
            [11] = "Tampering protection of throttle",
            [12] = "Error on all ignition trigger outputs",
            [13] = "ECU not correctly programmed",
            [14] = "Forced fuelcut by user",
            [15] = "Transmission requests fuelcut",
            [16] = "Kill engine, after engine has started, rpm too low",
            [20] = "Application conditions for fuel cut -SAAB",
            [21] = "Application conditions for fuel cut -OPEL",
            [31] = "Power management fuelcut on one cylinder",
            [32] = "Power management fuelcut on two cylinders",
            [33] = "Power management fuelcut on three cylinders",
            [34] = "Power management fuelcut on four cylinders",
            [35] = "Power management fuelcut on five cylinders",
            [36] = "Power management fuelcut on six cylinders",
        };

        private static readonly Dictionary<int, string> s_lambda = new()
        {
            [0] = "Closed loop activated",
            [1] = "Closed loop not activated",
            [2] = "Load too low",
            [3] = "Fuel enrichment in progress (no knock)",
            [4] = "Fuel enrichment in progress (knock)",
            [5] = "CW temp too low, closed throttle",
            [6] = "CW temp too low, open throttle",
            [7] = "Engine speed too low",
            [8] = "Negative throttle transient in progress",
            [9] = "Positive throttle transient in progress",
            [10] = "Fuel cut",
            [11] = "Throttle in limp home",
            [12] = "Diagnostic failure that affects the lambda control",
            [13] = "Engine not started",
            [14] = "Waiting number of combustion before hardware check",
            [15] = "Waiting until engine probe is warm",
            [16] = "Waiting until number of combustions have past after probe is warm",
            [17] = "Hot soak in progress",
            [18] = "SAI: Number of combustion to start closed loop has not passed",
            [19] = "Lambda integrator is frozen to 0 by SAI lean clamp",
            [20] = "Catalyst diagnose for V6 controls the fuel",
            [21] = "Lambda start not finished",
            [22] = "Lambda probe diagnose request open loop",
        };

        private static readonly Dictionary<int, string> s_airDemand = new()
        {
            [10] = "PedalMap",
            [11] = "Cruise control",
            [12] = "Idle control",
            [20] = "Max engine torque",
            [21] = "Traction control",
            [22] = "Manual gearbox limit",
            [23] = "Automatic gearbox limit",
            [24] = "Stall limit (Aut)",
            [25] = "Special mode",
            [26] = "Reverse limit",
            [27] = "Max vehicle speed",
            [28] = "Brake management",
            [29] = "System action",
            [30] = "Max engine speed",
            [31] = "Max vehicle speed",
            [40] = "Min load",
            [41] = "Min load",
            [50] = "Knock airmass limit",
            [51] = "Max engine speed",
            [52] = "Max turbo speed",
            [53] = "Max turbo speed",
            [54] = "Crankcase vent error",
            [55] = "Faulty APC",
            [61] = "Engine tipin limit",
            [62] = "Engine tipout limit",
        };

        // an unknown value shows the number
        public override string AirDemand(int value) => s_airDemand.TryGetValue(value, out string s) ? s : value.ToString();
        public override string Lambda(int value) => s_lambda.TryGetValue(value, out string s) ? s : value.ToString();
        public override string Fuelcut(int value) => s_fuelcut.TryGetValue(value, out string s) ? s : value.ToString();
    }

    /// <summary>
    /// T8Suite's passes over GMLAN. With "Prefer dynamic retrieval of live data" the table is one dynamic list (3B 17, read with
    /// 1A 18); rows that can't be in it (no SRAM address, more than 16 bytes) are read by address, which T8Suite didn't do. After
    /// a failure of the list every row is read by address for the rest of the session, as in T8Suite. T8Suite matched the
    /// list's data to the rows by counting the rows, so one row that couldn't be in the list shifted every later row's value;
    /// here the data goes to the rows it was read for.
    /// </summary>
    public sealed class T8RealtimeEngine(T8Ecu ecu, bool preferDynamic) : RealtimeEngine
    {
        private bool m_byAddress = !preferDynamic;
        private RealtimeSymbol[] m_list;

        protected override bool Connected => ecu.IsConnected;

        // the passes send tester present themselves (StallKeepAlive, as T8Suite set it before each pass); it comes back afterwards
        public override Task BeginAsync()
        {
            m_list = null;
            return ecu.RunAsync(t => { t.StallKeepAlive = true; });
        }

        // a disconnect ends the stall itself (T8Ecu.Close)
        public override async Task EndAsync()
        {
            if (ecu.IsConnected) await ecu.RunAsync(t => { t.StallKeepAlive = false; });
        }

        protected override Task<RealtimeSample> PassAsync(RealtimeSymbol[] rows, double fps) => ecu.RunAsync(t =>
        {
            if (!ecu.IsConnected) return null;
            Dictionary<RealtimeSymbol, byte[]> dynamic = m_byAddress ? null : Dynamic(t, rows);
            return Realtime.Cycle(rows, row => dynamic != null && dynamic.TryGetValue(row, out byte[] d) ? d : ByAddress(t, row), DateTime.Now,
                T8Realtime.Rules, fps, everyPass: dynamic != null);
        });

        // GetSRAMVarsFromTableOld: readMemoryNew per row, a failed read keeps the last value
        private static byte[] ByAddress(Trionic8 t, RealtimeSymbol row) =>
            row.SramAddress > 0 && row.Length > 0 ? t.readMemoryNew((int)row.SramAddress, row.Length, 0x40, false) : null;

        /// <summary>The rows the dynamic list can hold: read from SRAM, 1 to 16 bytes.</summary>
        internal static bool InList(RealtimeSymbol row) => !row.Derived && row.Length is >= 1 and <= 16 && row.SramAddress >= 0x100000;

        /// <summary>The list's answer, the rows' bytes one after the other, back to the rows they were read for.</summary>
        internal static Dictionary<RealtimeSymbol, byte[]> Split(RealtimeSymbol[] list, byte[] buf)
        {
            var data = new Dictionary<RealtimeSymbol, byte[]>(list.Length);
            int at = 0;
            foreach (RealtimeSymbol r in list)
            {
                data[r] = buf[at..(at + r.Length)];
                at += r.Length;
            }
            return data;
        }

        /// <summary>GetSRAMVarsFromTableDynamic: the list configured again when the table changed, then read; null after a failure.</summary>
        private Dictionary<RealtimeSymbol, byte[]> Dynamic(Trionic8 t, RealtimeSymbol[] rows)
        {
            RealtimeSymbol[] wanted = rows.Where(InList).ToArray();
            if (wanted.Length == 0) return [];
            if (m_list == null || !m_list.SequenceEqual(wanted))
            {
                var list = wanted.Select(r => new ITrionic.dynAddrHelper { address = (int)r.SramAddress, size = (byte)r.Length }).ToList();
                if (!t.ConfigureDynamicListByAddress(list)) return Fail();
                m_list = wanted;
            }
            byte[] buf = t.ReadDynamicSymbols();
            return buf != null && buf.Length == m_list.Sum(r => r.Length) ? Split(m_list, buf) : Fail();
        }

        // "Forcing readByAddress for the duration of this session"
        private Dictionary<RealtimeSymbol, byte[]> Fail()
        {
            m_byAddress = true;
            m_list = null;
            return null;
        }
    }
}
