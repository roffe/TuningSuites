using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommonSuite;
using TrionicCANLib.API;

namespace T7
{
    /// <summary>T7Suite's realtime table: frmMain.FillRealtimeTable, its signed list, per-cylinder counters, status texts and maps.</summary>
    public sealed class T7Realtime : RealtimeRules
    {
        public static T7Realtime Rules { get; } = new();

        public override IReadOnlySet<string> Signed { get; } = new HashSet<string>
        {
            "ActualIn.T_Engine", "ActualIn.T_AirInlet", "Out.fi_Ignition", "Out.M_Engine", "ECMStat.P_Engine", "ECMStat.p_Diff",
            "IgnProt.fi_Offset", "IgnKnk.fi_MeanKnock", "Ign.fi_OtherOff", "IgnJerkProt.fi_Offset", "Lambda.LambdaInt",
            "MAF.m_AirInlet", "AdpFuelProt.MulFuelAdapt", "BoostProt.PFac", "BoostProt.IFac", "BoostProt.LoadDiff",
        };

        // the 8 / 12 byte per-cylinder counters
        public override IReadOnlyDictionary<string, (string Prefix, int Size)> PerCylinder { get; } = new Dictionary<string, (string, int)>
        {
            ["KnkDet.KnockCyl"] = ("KnockCyl", 2),
            ["KnkDetAdap.KnkCntCyl"] = ("KnockCyl", 2),
            ["MissfAdap.MissfCntCyl"] = ("MisfCyl", 2),
        };

        public override DashboardSymbols Symbols { get; } = new("Out.M_Engine", "IgnProt.fi_Offset", "m_Request", "Out.X_AccPedal");

        public override string LogExtension => "t7l";

        public override string LogFilesName => "Trionic 7 logfiles";

        public override string LayoutExtension => "t7rtl";

        public override List<RealtimeSymbol> Dashboard(SuiteBinary bin, AppSettings settings)
        {
            var rows = new List<RealtimeSymbol>
            {
                Row(bin, "ActualIn.n_Engine", "Engine speed", 0, 1, 0, 8000),
                Row(bin, "In.v_Vehicle", "Vehicle speed", 0, 0.1, 0, 300, 3),
                Row(bin, "Out.X_AccPedal", "TPS %", 0, 0.1, 0, 100),
                Row(bin, "ActualIn.T_Engine", "Engine temperature", 0, 1, -20, 120, 5),
                Row(bin, "ActualIn.T_AirInlet", "Intake air temperature", 0, 1, -20, 120, 3),
                Row(bin, "ECMStat.ST_ActiveAirDem", "Active air demand map", 0, 1, 0, 255),
                Row(bin, "Lambda.Status", "Lambda status", 0, 1, 0, 255),
                Row(bin, "FCut.CutStatus", "Fuelcut status", 0, 1, 0, 255),
                Row(bin, "IgnProt.fi_Offset", "Ignition offset", 0, 0.1, -20, 20),
                Row(bin, "m_Request", "Requested airmass", 0, 1, 0, 600),
                Row(bin, "Out.M_Engine", "Calculated torque", 0, 1, 0, 600),
                Row(bin, "In.p_AirInlet", "Boost", -1, 0.001, -1, 3),
                Row(bin, "Out.PWM_BoostCntrl", "Duty cycle BCV", 0, 0.1, 0, 100),
                Row(bin, "Out.fi_Ignition", "Ignition advance", 0, 0.1, -10, 50),
                Row(bin, "MAF.m_AirInlet", "Actual airmass", 0, 1, 0, 1600),
            };
            if (Realtime.HasEgtCalculation(bin)) rows.Add(Row(bin, "Exhaust.T_Calc", "Calculated EGT temperature", 0, 1, 0, 1200, 2));
            rows.Add(Row(bin, "BFuelProt.CurrentFuelCon", "Fuel consumption", 0, 0.1, 0, 50, 2));
            if (!settings.UseWidebandLambda) rows.Add(Row(bin, "Lambda.LambdaInt", "Lambda value (nbO2)", 1, 0.0001, 0, 2));
            else
            {
                double correction = settings.WideBandSymbol == "DisplProt.LambdaScanner" ? 0.1 : 1;
                rows.Add(Row(bin, settings.WideBandSymbol, "Lambda value (wbO2)", 0, correction, 10, 20));
            }
            return rows.Where(r => r != null).ToList();
        }

        public override RealtimeSymbol FromSymbol(SymbolHelper sh)
        {
            string name = sh.Varname.StartsWith("Symbol") && sh.Userdescription != "" ? sh.Userdescription : sh.Varname;
            var (min, max, correction) = name switch
            {
                "Torque.M_MaxEngAndGear" => (0d, 255d, 1d),
                "BFuelProt.t_InjActual" => (0, 255, 0.001),
                "ActualIn.v_Vehicle2" or "In.v_Vehicle" => (0, 255, 0.1),
                "ActualIn.U_LambdaCat" or "ActualIn.U_LambdaEng" => (0, 1200, 1),
                "TorqueProt.m_AirTMasLim" or "AirctlData.Actual" => (0, 1800, 1),
                _ => (0, sh.Length == 1 ? 255 : 65535, 1),
            };
            return UserRow(sh, name, min, max, correction);
        }

        public override IReadOnlyList<CellRule> CellRules { get; } =
        [
            new(["BFuelCal.Map", "BFuelCal.StartMap", "BFuelCal.E85Map", "BFuelCal.GasMap", "MyrtilosCal.Fuel_GasMap", "MyrtilosAdap.WBLambda_FeedbackMap", "MyrtilosAdap.WBLambda_FFMap"],
                "BFuelCal.AirXSP", CellInput.Airmass, 1, "BFuelCal.RpmYSP", CellInput.Rpm, 1),
            new(["KnkFuelCal.EnrichmentMap"], "IgnKnkCal.m_AirXSP", CellInput.Airmass, 1, "IgnKnkCal.n_EngYSP", CellInput.Rpm, 1),
            new(["InjAnglCal.Map"], "InjAnglCal.AirXSP", CellInput.Airmass, 1, "InjAnglCal.RpmYSP", CellInput.Rpm, 1),
            new(["IgnNormCal.Map", "IgnNormCal.GasMap", "IgnE85Cal.fi_AbsMap"], "IgnNormCal.m_AirXSP", CellInput.Airmass, 1, "IgnNormCal.n_EngYSP", CellInput.Rpm, 1),
            new(["KnkFuelCal.fi_MapMaxOff"], "KnkFuelCal.m_AirXSP", CellInput.Airmass, 1, "BstKnkCal.n_EngYSP", CellInput.Rpm, 1),
            new(["IgnKnkCal.IndexMap"], "IgnKnkCal.m_AirXSP", CellInput.Airmass, 1, "IgnKnkCal.n_EngYSP", CellInput.Rpm, 1),
            new(["KnkDetCal.RefFactorMap"], "KnkDetCal.m_AirXSP", CellInput.Airmass, 1, "KnkDetCal.n_EngYSP", CellInput.Rpm, 1),
            new(["PedalMapCal.m_RequestMap"], "PedalMapCal.n_EngineMap", CellInput.Rpm, 1, "PedalMapCal.X_PedalMap", CellInput.Tps, 0.1),
            new(["TorqueCal.m_AirTorqMap"], "TorqueCal.M_EngXSP", CellInput.Torque, 1, "TorqueCal.n_EngYSP", CellInput.Rpm, 1),
            new(["TorqueCal.M_NominalMap"], "TorqueCal.m_AirXSP", CellInput.Airmass, 1, "TorqueCal.n_EngYSP", CellInput.Rpm, 1),
            new(["BoostCal.RegMap"], "BoostCal.SetLoadXSP", CellInput.Airmass, 1, "BoostCal.n_EngSP", CellInput.Rpm, 1),
            new(["BstKnkCal.MaxAirmass"], "BstKnkCal.OffsetXSP", CellInput.IgnitionOffset, 0.1, "BstKnkCal.n_EngYSP", CellInput.Rpm, 1),
        ];

        public override string AirDemand(int value) => RealtimeStatus.AirDemand(value);
        public override string Lambda(int value) => RealtimeStatus.Lambda(value);
        public override string Fuelcut(int value) => RealtimeStatus.Fuelcut(value);

        /// <summary>ReadSymbolFromSRAM: up to 4 bytes by SRAM address (data from byte 1 of the reply), longer by symbol number; null when it can't or didn't.</summary>
        public static byte[] Read(Trionic7 t, RealtimeSymbol row)
        {
            bool ok;
            if (row.Length is > 0 and <= 4)
            {
                if (row.SramAddress <= 0) return null;
                byte[] reply = t.ReadValueFromSRAM(row.SramAddress, row.Length, out ok);
                return ok && reply.Length > row.Length ? reply[1..(row.Length + 1)] : null;
            }
            if (row.Length <= 4 || row.SymbolNumber < 0) return null;
            byte[] data = t.ReadSymbolNumber((uint)row.SymbolNumber, out ok);
            return ok ? data : null;
        }
    }

    /// <summary>T7Suite's passes over KWP, with Performance.Mode every 21 passes; the alive polling rests while they run.</summary>
    public sealed class T7RealtimeEngine(T7Ecu ecu) : RealtimeEngine
    {
        private int m_pass;

        /// <summary>Performance.Mode, read every 21 passes when the bin has it.</summary>
        public SymbolHelper PerformanceMode { get; set; }

        protected override bool Connected => ecu.IsConnected;

        public override Task BeginAsync() => ecu.RunAsync(t => t.SuspendAlivePolling());

        public override async Task EndAsync()
        {
            if (ecu.IsConnected) await ecu.RunAsync(t =>
            {
                if (ecu.IsConnected) t.ResumeAlivePolling();
            });
        }

        protected override Task<RealtimeSample> PassAsync(RealtimeSymbol[] rows, double fps)
        {
            bool mode = ++m_pass % 21 == 0 && PerformanceMode != null;
            return ecu.RunAsync(t =>
            {
                // the session closed while this pass waited (see RealtimeEngine.PassAsync)
                if (!ecu.IsConnected) return null;
                int? performance = mode ? ReadPerformanceMode(t, PerformanceMode) : null;
                return Realtime.Cycle(rows, row => T7Realtime.Read(t, row), System.DateTime.Now, T7Realtime.Rules, fps, performance);
            });
        }

        // the last byte: 0 / 'E' eco, 1 / 'N' normal, 2 / 'S' sport
        private static int? ReadPerformanceMode(Trionic7 t, SymbolHelper sh)
        {
            byte[] data = T7Realtime.Read(t, new RealtimeSymbol { Name = sh.SmartVarname, SymbolNumber = sh.Symbol_number, SramAddress = sh.Start_address, Length = sh.Length });
            return data is { Length: > 0 } ? data[^1] switch { 0 or (byte)'E' => 0, 1 or (byte)'N' => 1, 2 or (byte)'S' => 2, _ => null } : null;
        }

        /// <summary>SetPerformanceMode: the mode in the last byte of Performance.Mode, written by SRAM address.</summary>
        public Task<bool> SetPerformanceModeAsync(int mode) => ecu.RunAsync(t =>
        {
            if (PerformanceMode is not { Length: > 0 } sh) return false;
            var data = new byte[sh.Length];
            data[^1] = (byte)mode;
            return t.WriteMapToSRAM((uint)sh.Start_address, data);
        });
    }
}
