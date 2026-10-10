using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommonSuite;

namespace Trionic5Tools
{
    /// <summary>The realtime panel's tabs in T5Suite's order (its RealtimeMonitoringType names).</summary>
    public enum T5RealtimeTab { Fuel, Ignition, Boost, Knock, Dashboard, Settings, Userdefined, AutotuneFuel, AutotuneIgnition, EngineStatus, OnlineGraph, UserMaps }

    /// <summary>
    /// T5Suite's realtime values on the shared panel: the symbols its watch lists polled (FillRealtimePool; each panel tab polls its WatchList),
    /// Trionic5SymbolConverter's conversions as row corrections plus Decode, the Pgm_status texts and the live cells.
    /// ponytail: the open bin's MAP sensor and the wideband settings are kept from Dashboard, one panel at a time.
    /// </summary>
    public sealed class T5Realtime : RealtimeRules
    {
        public static T5Realtime Rules { get; } = new();

        private int m_percent = 100;
        private AppSettings m_settings;

        /// <summary>The lambda row of the open bin's table: the wideband symbol, else AD_sond (set by Dashboard).</summary>
        public string LambdaSymbol { get; private set; } = "AD_sond";

        private static readonly string[] Base = ["P_medel", "Lufttemp", "Kyl_temp", "Rpm", "Medeltrot", "Regl_tryck"];
        private static readonly string[] Enrichments = ["Lacc_mangd", "Acc_mangd", "Lret_mangd", "Ret_mangd"];

        /// <summary>
        /// FillRealtimePool: what a panel tab reads. The base (the autotune tabs their own), Pgm_status, the lambda symbol, the
        /// enrichments while the fuel autotune runs, Knock_offset1234 while the ignition autotune runs (T5Suite kept the last knock
        /// state on other tabs), then the tab's own. User rows are the caller's (User defined, and the first list).
        /// </summary>
        /// <summary>The lambda input: the wideband symbol when one is used, else the narrowband sond.</summary>
        public static string LambdaFor(AppSettings settings) =>
            settings.UseWidebandLambda && settings.WideBandSymbol is "AD_EGR" or "AD_cat" or "AD_sond" ? settings.WideBandSymbol : "AD_sond";

        public static IReadOnlySet<string> WatchList(T5RealtimeTab tab, bool fuelAutotune, bool ignitionAutotune, string lambda)
        {
            var names = new HashSet<string>(tab switch
            {
                T5RealtimeTab.AutotuneIgnition => ["P_medel", "Rpm", "Knock_offset1234"],
                T5RealtimeTab.AutotuneFuel => ["P_medel", "Kyl_temp", "Rpm", "Medeltrot"],
                _ => Base,
            }) { "Pgm_status", lambda };
            if (fuelAutotune) names.UnionWith(Enrichments);
            if (ignitionAutotune) names.Add("Knock_offset1234");
            names.UnionWith(tab switch
            {
                T5RealtimeTab.Fuel => ["Insptid_ms10", .. Enrichments],
                T5RealtimeTab.Ignition => ["Ign_angle", "Knock_offset1", "Knock_offset2", "Knock_offset3", "Knock_offset4"],
                T5RealtimeTab.Boost => ["Max_tryck", "Apc_decrese", "P_fak", "I_fak", "D_fak", "PWM_ut10"],
                T5RealtimeTab.Knock => ["Knock_count_cyl1", "Knock_count_cyl2", "Knock_count_cyl3", "Knock_count_cyl4", "Knock_offset1", "Knock_offset2",
                    "Knock_offset3", "Knock_offset4", "Knock_offset1234", "Apc_decrese", "Knock_diag_level"],
                T5RealtimeTab.Dashboard => ["Bil_hast", "TQ", "Insptid_ms10", "Apc_decrese", "Ign_angle"],
                T5RealtimeTab.OnlineGraph => ["TQ", "Insptid_ms10", "Ign_angle", "PWM_ut10"],
                _ => [],
            });
            return names;
        }

        public override IReadOnlySet<string> Signed { get; } = new HashSet<string>
        {
            "Ign_angle", "Knock_offset1", "Knock_offset2", "Knock_offset3", "Knock_offset4", "Knock_offset1234", "P_fak", "I_fak", "D_fak",
        };

        // the enrichments: a byte per cylinder, cylinder 1 first
        public override IReadOnlyDictionary<string, (string Prefix, int Size)> PerCylinder { get; } = new Dictionary<string, (string, int)>
        {
            ["Lacc_mangd"] = ("LoadAccCyl", 1), ["Acc_mangd"] = ("TPSAccCyl", 1), ["Lret_mangd"] = ("LoadRetCyl", 1), ["Ret_mangd"] = ("TPSRetCyl", 1),
        };

        // no airmass, air demand or consumption symbols on T5; the narrowband sond is the lambda value
        public override DashboardSymbols Symbols { get; } = new("TQ", "Knock_offset1234", "", "Medeltrot", Speed: "Bil_hast", Boost: "P_medel",
            DutyCycle: "PWM_ut10", IgnitionAdvance: "Ign_angle", Airmass: "", Rpm: "Rpm", Coolant: "Kyl_temp", IntakeAir: "Lufttemp", Egt: "EGT",
            ActiveAirDemand: "", FuelConsumption: "", Power: "", LambdaInt: "AD_sond", LambdaStatus: "Pgm_status", Fuelcut: "Pgm_status");

        public override string LogExtension => "t5l";

        public override string LogFilesName => "Trionic 5 logfiles";

        public override string LayoutExtension => "t5rtl";

        /// <summary>
        /// The table: every symbol T5Suite's panel tabs read, with its conversions; boost values follow the MAP sensor (× 1.2 for
        /// 3.0 bar and so on). Each tab polls its WatchList of them. Symbols the bin lacks are left out (T5Suite read SRAM 0 for them).
        /// </summary>
        public override List<RealtimeSymbol> Dashboard(SuiteBinary bin, AppSettings settings)
        {
            m_settings = settings;
            m_percent = (bin as T5Binary)?.SensorPercent ?? 100;
            double bar = m_percent / 10000.0;
            var rows = new List<RealtimeSymbol>
            {
                Row(bin, "Rpm", "Engine speed (rpm)", 0, 10, 0, 8000),
                Row(bin, "P_medel", "Boost (bar)", -1, bar, -1, 2.5),
                Row(bin, "Regl_tryck", "Target boost (bar)", -1, bar, -1, 2.5),
                Row(bin, "Max_tryck", "Boost request (bar)", -1, bar, -1, 2.5),
                Row(bin, "Medeltrot", "Throttle position", -34, 1, 0, 255),
                Row(bin, "Kyl_temp", "Coolant temperature (°C)", 0, 1, -40, 120),
                Row(bin, "Lufttemp", "Intake air temperature (°C)", 0, 1, -40, 100),
                Row(bin, "Pgm_status", "Program status", 0, 1, 0, 0xFFFFFFFFFFFF),
                Row(bin, "Insptid_ms10", "Injection time (ms)", 0, 0.1, 0, 30),
                Row(bin, "Lacc_mangd", "Enrich load accel", 0, 1, 0, uint.MaxValue),
                Row(bin, "Acc_mangd", "Enrich TPS accel", 0, 1, 0, uint.MaxValue),
                Row(bin, "Lret_mangd", "Enlean load accel", 0, 1, 0, uint.MaxValue),
                Row(bin, "Ret_mangd", "Enlean TPS accel", 0, 1, 0, uint.MaxValue),
                Row(bin, "Ign_angle", "Ignition advance (°)", 0, 0.1, -10, 45),
                Row(bin, "Knock_offset1234", "Knock offset (°)", 0, 0.1, 0, 20),
                Row(bin, "Apc_decrese", "Boost reduction (bar)", 0, bar, 0, 2.5),
                Row(bin, "P_fak", "P factor", 0, 1, -32768, 32000),
                Row(bin, "I_fak", "I factor", 0, 1, -32768, 32000),
                Row(bin, "D_fak", "D factor", 0, 1, -32768, 32000),
                Row(bin, "PWM_ut10", "PWM output (%)", 0, 1, 0, 100),
                Row(bin, "Knock_diag_level", "Knock diagnosis level", 0, 1, 0, 255),
                Row(bin, "Bil_hast", "Vehicle speed (km/h)", 0, 1, 0, 300),
                Row(bin, "TQ", "Torque (Nm)", 0, m_percent / 100.0, 0, 800),
            };
            for (int c = 1; c <= 4; c++) rows.Add(Row(bin, "Knock_offset" + c, $"Ignition offset cyl #{c} (°)", 0, 0.1, 0, 20));
            for (int c = 1; c <= 4; c++)
            {
                // T5Suite read the knock counters as 2 bytes whatever the table said
                if (Row(bin, "Knock_count_cyl" + c, $"Knocks cyl #{c}", 0, 1, 0, 65535) is { } knock)
                {
                    knock.Length = 2;
                    rows.Add(knock);
                }
            }
            string lambda = LambdaSymbol = LambdaFor(settings);
            rows.Add(lambda == "AD_sond" ? Row(bin, "AD_sond", "Lambda (narrowband)", 0, 1, 0, 2) : Row(bin, lambda, "Wideband AFR", 0, 1, 7, 24));
            return rows.Where(r => r != null).ToList();
        }

        /// <summary>
        /// ConvertSymbol's code paths: the temperatures signed above 128, the narrowband sond as λ (|raw − 125| / 100; T5Suite showed it
        /// × 14.7 as AFR), the wideband inputs as AFR over 0..255 (T7's count is 0..1023), Pgm_status little-endian (48 bits). User rows
        /// are raw.
        /// </summary>
        public override double Decode(RealtimeSymbol row, byte[] d)
        {
            if (!row.UserDefined && d.Length > 0)
            {
                switch (row.Name)
                {
                    case "Kyl_temp" or "Lufttemp" when d.Length == 1:
                        return d[0] > 128 ? d[0] - 256 : d[0];
                    case "AD_sond":
                        return Math.Abs(d[0] - 125) / 100.0;
                    case "AD_EGR" or "AD_cat" when m_settings != null:
                        return WidebandAfr.AdcToAfr(d[0] * 1023.0 / 255, m_settings);
                    case "Pgm_status":
                        long v = 0;
                        for (int i = Math.Min(d.Length, 6) - 1; i >= 0; i--) v = v << 8 | d[i];
                        return v;
                }
            }
            return base.Decode(row, d);
        }

        public override RealtimeSymbol FromSymbol(SymbolHelper sh) => UserRow(sh, sh.SmartVarname, 0, sh.Length == 1 ? 255 : 65535, 1);

        // RealtimeSymbolCollection: SRAM symbols of 1 to 4 bytes
        public override bool CanPoll(SymbolHelper sh) => sh.Start_address > 0 && sh.Length is >= 1 and <= 4;

        /// <summary>
        /// T5Suite matched viewers by their axis captions: "MAP" against the boost (axis × sensor factor × 0.01 − 1 bar), "RPM" and
        /// "Throttle position" against the values as read. The "Pressure error (bar)" maps aren't tracked.
        /// </summary>
        public override IReadOnlyList<CellRule> CellRules =>
        [
            new(["Insp_mat!", "Inj_map_0!", "Fuel_knock_mat!", "Ign_map_0!", "Ign_map_2!", "Ign_map_4!"], "", CellInput.Boost, m_percent / 10000.0, "", CellInput.Rpm, 1, -1),
            new(["Tryck_mat!", "Tryck_mat_a!", "Reg_kon_mat!", "Reg_kon_mat_a!"], "", CellInput.Tps, 1, "", CellInput.Rpm, 1),
        ];

        public override string AirDemand(int value) => "";

        /// <summary>The panel's "Idle" / "Closed loop" LEDs (Pgm_status 0x40000000 / 0x02000000).</summary>
        public override string Lambda(int value) => (value & 0x40000000) != 0 ? "Idle" : (value & 0x02000000) != 0 ? "Closed loop" : "Open loop";

        /// <summary>Pgm_status 0x20 fuel cut, 0x8000 / 0x4000 / 0x2000 / 0x1000 fuel cut cylinder 1 / 2 / 3 / 4.</summary>
        public override string Fuelcut(int value)
        {
            if ((value & 0x20) != 0) return "Fuel cut";
            var cylinders = Enumerable.Range(1, 4).Where(c => (value & (0x10000 >> c)) != 0).ToList();
            return cylinders.Count > 0 ? "Fuel cut cyl " + string.Join(", ", cylinders) : "No fuelcut";
        }
    }

    /// <summary>T5's passes: every row read from SRAM by address over the terminal (6 bytes per round trip).</summary>
    public sealed class T5RealtimeEngine(T5Ecu ecu) : RealtimeEngine
    {
        protected override bool Connected => ecu.IsConnected;

        protected override Task<RealtimeSample> PassAsync(RealtimeSymbol[] rows, double fps) => ecu.RunAsync(t =>
        {
            // the session closed while this pass waited (see RealtimeEngine.PassAsync)
            if (!ecu.IsConnected) return null;
            // every row on every pass, as T5Suite read its watch list (no Reload delays: a row coming back to a tab isn't stale)
            return Realtime.Cycle(rows, row => row.SramAddress > 0 && row.Length > 0 ? t.readRAM((ushort)row.SramAddress, (uint)row.Length) : null,
                DateTime.Now, T5Realtime.Rules, fps, everyPass: true);
        });
    }
}
