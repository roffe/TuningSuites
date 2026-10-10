using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommonSuite;
using TrionicCANLib.Checksum;

namespace Trionic5Tools
{
    /// <summary>
    /// An opened T5 binary (T5.2: 128 KB, T5.5: 256 KB) for the shared windows, over the lifted Trionic5File: its symbols, maps,
    /// axes and checksum as T5Suite read them. Flash addresses sit at the top of the 512 KB address space, so the file offset is
    /// the address minus (0x80000 − file length); symbols without one only live in SRAM.
    /// </summary>
    public class T5Binary : SuiteBinary
    {
        /// <summary>The lifted file logic (firmware properties, tuning, anomalies), per binary.</summary>
        public Trionic5File File { get; }

        public Trionic5FileInformation Info { get; }

        private T5Binary(string fileName, Trionic5File file, Trionic5FileInformation info) : base(fileName, info.SymbolCollection)
        {
            File = file;
            Info = info;
        }

        // a project's rebuild: bytes only
        private T5Binary(string fileName) : base(fileName, new SymbolCollection())
        {
            File = new Trionic5File();
            File.SelectFile(fileName);
            Info = new Trionic5FileInformation { Filelength = (int)new FileInfo(fileName).Length };
            File.FileInfo = Info;
        }

        /// <summary>A T5.2 (0x20000) or T5.5 (0x40000) file.</summary>
        public static bool IsValidFile(string fileName) =>
            System.IO.File.Exists(fileName) && new FileInfo(fileName).Length is 0x20000 or 0x40000;

        /// <summary>frmMain.OpenFile: the symbol table and the address lookup, parsed by Trionic5File.</summary>
        public static T5Binary Open(string fileName)
        {
            var file = new Trionic5File();
            file.SelectFile(fileName);
            Trionic5FileInformation info = file.ParseFile();
            // the grid's Description column showed the help text, grouped by the XDF category and subcategory
            foreach (SymbolHelper sh in info.SymbolCollection)
            {
                sh.Description = sh.Helptext;
                sh.Category = sh.XdfCategory.ToString();
                sh.Subcategory = sh.XdfSubcategory.ToString();
            }
            return new T5Binary(fileName, file, info);
        }

        public override SuiteBinary RawFile(string fileName) => new T5Binary(fileName);

        public override int FileLength => Info.Filelength;

        /// <summary>frmBinmerger: two EPROM halves of the same length interleaved, second[i] then first[i]; null when the lengths differ.</summary>
        public static byte[] Merge(byte[] first, byte[] second) =>
            first.Length != second.Length ? null : first.Zip(second, (a, b) => new[] { b, a }).SelectMany(x => x).ToArray();

        /// <summary>Split binary file: even offsets to chip2, odd to chip1 (Merge(chip1, chip2) gives the file back).</summary>
        public static (byte[] chip1, byte[] chip2) Split(byte[] data) =>
            (data.Where((_, i) => i % 2 == 1).ToArray(), data.Where((_, i) => i % 2 == 0).ToArray());

        /// <summary>T5.5 or T5.2, from the file length.</summary>
        public bool IsTrionic55 => FileLength == 0x40000;

        /// <summary>Trionic5File.GetSymbolAddress: a flash address as a file offset.</summary>
        public int FlashToFile(long flash) => (int)(flash - (0x80000 - FileLength));

        public override int FileAddress(SymbolHelper sh) =>
            sh.Flash_start_address > 0 && FlashToFile(sh.Flash_start_address) is var a and >= 0 && a < FileLength ? a : -1;

        public override long SymbolAddress(string symbolname) => Find(symbolname) is { } sh && FileAddress(sh) is var a and >= 0 ? a : 0;

        /// <summary>Compare and transfer: the file offset, -1 (skipped) for the SRAM-only symbols.</summary>
        public override long AddressOf(SymbolHelper sh) => FileAddress(sh);

        /// <summary>
        /// The maps ("!" names, the ones in flash): SRAM compares cover them (StartCompareToSRAMFile took flash and SRAM symbols),
        /// and compare lists the ones only one file has.
        /// </summary>
        public override bool IsCalibration(string name) => name.EndsWith('!') || Find(name) is { Flash_start_address: > 0 };

        /// <summary>Maps live in SRAM too (at Start_address); the ECU reads and writes them there.</summary>
        public override bool InSram(SymbolHelper sh) => sh.Start_address > 0;

        /// <summary>Trionic5File.WriteData: every write stamps the file's sync date (length − 0x1E0), which the ECU synchronization compares.</summary>
        protected override void Written(int address, int length)
        {
            if (Symbols.Count > 0) File.SetMemorySyncDate(DateTime.Now);
        }

        /// <summary>The sync date at length − 0x1E0; 2000-01-01 when the file has none.</summary>
        public DateTime SyncDate => File.GetMemorySyncDate();

        public override byte[] Read(int address, int length) => length <= 0 ? new byte[1] : File.ReadData((uint)address, (uint)length);

        public override void UpdateChecksum()
        {
            File.UpdateChecksum();
            if (!File.ValidateChecksum()) throw new InvalidOperationException($"The checksum of {Path.GetFileName(FileName)} does not verify after updating it.");
        }

        public override ChecksumResult VerifyChecksum() => File.ValidateChecksum() ? ChecksumResult.Ok : ChecksumResult.Invalid;

        public override string Describe(string symbolname)
        {
            new SymbolTranslator().TranslateSymbolToHelpText(symbolname, out string help, out _, out _);
            return help;
        }

        public override bool ImportXmlSymbols(string file) => false;

        public override (string xAxis, string yAxis, string xDescr, string yDescr, string zDescr) AxisSymbols(string symbolname)
        {
            var sat = new SymbolAxesTranslator();
            File.GetMapAxisDescriptions(symbolname, out string x, out string y, out string z);
            return (sat.GetXaxisSymbol(symbolname), sat.GetYaxisSymbol(symbolname), x, y, z);
        }

        public override int[] GetXaxisValues(string symbolname) => File.GetMapXaxisValues(symbolname) ?? [];

        public override int[] GetYaxisValues(string symbolname) => File.GetMapYaxisValues(symbolname) ?? [];

        public override int TableWidth(string symbolname)
        {
            File.GetMapMatrixWitdhByName(symbolname, out int columns, out _);
            return Math.Max(columns, 1);
        }

        public override bool IsSixteenBitTable(string symbolname) => File.IsTableSixteenBits(symbolname);

        public override double GetMapCorrectionFactor(string symbolname) => File.GetCorrectionFactorForMap(symbolname);

        public override double GetMapCorrectionOffset(string symbolname) => File.GetOffsetForMap(symbolname);

        public override IReadOnlyList<string> FixedPackageSymbols { get; } = [];

        /// <summary>
        /// The "Manual tuning" ribbon page's map buttons in their groups and order (Trionic5FileInformation's getters). Captions
        /// never change; T5.2 lacks the manual 1st / 2nd gear boost limits and the ignition retard limit, and some symbols differ.
        /// </summary>
        public override List<MapShortcut> QuickMaps()
        {
            bool t55 = IsTrionic55;
            var m = new List<MapShortcut>();
            void Add(string group, string caption, string symbol, bool show = true)
            {
                if (show) m.Add(new MapShortcut(group, caption, symbol));
            }
            const string fuel = "Injection [ Fuel ]", ign = "Ignition", man = "Turbo control - manual gearbox", aut = "Turbo control - automatic gearbox",
                knock = "Knock detection", warm = "Engine warmup", idle = "Idle control";
            Add(fuel, "VE map - normal", Has("Inj_map_0!") ? "Inj_map_0!" : "Insp_mat!");
            Add(fuel, "VE map - knock", "Fuel_knock_mat!");
            Add(fuel, "Injector scaling", "Inj_konst!");
            Add(fuel, "Battery correction map", "Batt_korr_tab!");
            Add(ign, "Ignition map - normal", "Ign_map_0!");
            Add(ign, "Ignition map - knock", "Ign_map_2!");
            Add(ign, "Ignition map - warmup", "Ign_map_4!");
            Add(man, "Boost request map", "Tryck_mat!");
            Add(man, "Boost control bias", "Reg_kon_mat!");
            Add(man, "Fuel cut in overboost", "Tryck_vakt_tab!");
            Add(man, "P factors (PID)", "P_fors!");
            Add(man, "I factors (PID)", "I_fors!");
            Add(man, "D factors (PID)", "D_fors!");
            Add(man, "Boost limit in 1st gear", "Regl_tryck_fgm!", t55);
            Add(man, "Boost limit in 2nd gear", "Regl_tryck_sgm!", t55);
            Add(aut, "Boost request map", "Tryck_mat_a!");
            Add(aut, "Boost control bias", "Reg_kon_mat_a!");
            Add(aut, "Boost limit in 1st gear", t55 ? "Regl_tryck_fgaut!" : "Regl_tryck_fga!");
            Add(aut, "P factors (PID)", "P_fors_a!");
            Add(aut, "I factors (PID)", "I_fors_a!");
            Add(aut, "D factors (PID)", "D_fors_a!");
            Add(knock, "Knock sensitivity map", t55 ? "Knock_ref_matrix!" : "Knock_ref_tab!");
            Add(knock, "Ignition retard limit", "Knock_lim_tab!", t55);
            Add(knock, "Boost reduction map", "Apc_knock_tab!");
            Add(warm, "Afterstart enrichment (1)", "Eftersta_fak!");
            Add(warm, "Afterstart enrichment (2)", "Eftersta_fak2!");
            Add(idle, "Idle target RPM", "Idle_rpm_tab!");
            Add(idle, "Idle ignition", "Ign_idle_angle!");
            Add(idle, "Idle ignition correction", "Ign_map_1!");
            Add(idle, "Idle fuel map", "Idle_fuel_korr!");
            return m;
        }

        // ---- map viewer rules (MapViewerEx) ----

        /// <summary>The MAP sensor setting "Auto detect mapsensor type": the sensor from the maps when the marker says stock.</summary>
        public bool AutoDetectMapSensor { get; set; }

        /// <summary>The view's sensor scale: 120 for 3.0 bar, 140 / 160 / 200 for 3.5 / 4.0 / 5.0, 100 for the stock 2.5 bar.</summary>
        public int SensorPercent => File.GetMapSensorType(AutoDetectMapSensor) switch
        {
            MapSensorType.MapSensor30 => 120,
            MapSensorType.MapSensor35 => 140,
            MapSensorType.MapSensor40 => 160,
            MapSensorType.MapSensor50 => 200,
            _ => 100,
        };

        private static readonly string[] Scalable =
        [
            "Tryck_mat", "Regl_tryck", "Tryck_vakt_tab", "Idle_tryck", "Limp_tryck_konst", "Knock_press", "Turbo_knock_tab", "Open_loop", "Sond_heat_tab",
            "Reg_last!", "Idle_st_last!", "Lam_minlast!", "Lam_laststeg!", "Grund_last!", "Max_ratio_aut!", "Diag_speed_load!", "Turbo_knock_press",
            "Kadapt_load_high!", "Kadapt_load_low!", "Iv_min_load!", "Shift_load!", "Shift_up_load_hyst!", "Fload_tab!",
        ];

        /// <summary>MapIsScalableFor3Bar: the pressure maps a 3.0 to 5.0 bar sensor view scales.</summary>
        public override int ScalePercent(string symbolname) => Scalable.Any(symbolname.StartsWith) ? SensorPercent : 100;

        public override int AxisScalePercent(string caption) => caption is "MAP" or "Pressure error (bar)" ? SensorPercent : 100;

        /// <summary>
        /// MapViewerEx's signs: 16-bit values above 32000 are negative, I_kyl_st! / I_luft_st! / Last_temp_st! above 128; the
        /// injection maps take none (MapSupportsNegativeValues). T5Suite saved negative 8-bit values as 0.
        /// </summary>
        public override int? SignAbove(string symbolname)
        {
            bool sixteen = IsSixteenBitTable(symbolname);
            if (symbolname is "Insp_mat!" or "Inj_map_0!" or "Fuel_knock_mat!") return sixteen ? 0xFFFF : 0xFF;
            if (symbolname is "I_kyl_st!" or "I_luft_st!" or "Last_temp_st!" && !sixteen) return 128;
            return sixteen ? 32000 : null;
        }

        /// <summary>
        /// TryToAddOpenLoopTables: with lambda control on, Open_loop! (MAP per rpm row) on the injection and ignition maps and
        /// Open_loop_knock! on the knock fuel map.
        /// </summary>
        public override double[] OpenLoopLimits(string mapname)
        {
            string table = mapname switch
            {
                "Insp_mat!" or "Inj_map_0!" or "Ign_map_0!" => "Open_loop!",
                "Fuel_knock_mat!" => "Open_loop_knock!",
                _ => null,
            };
            if (table == null || Find(table) is not { } sh || FileAddress(sh) is not (var a and >= 0) || !File.GetTrionicProperties().Lambdacontrol) return null;
            // one byte per rpm row (the upside-down viewer's open_loop[length - 1 - display row] is the data row)
            return Read(a, sh.Length).Select(b => (double)b).ToArray();
        }

        public override int AddressTableStart(string file) => -1;

        public override void CopyAddressTable(string target) => throw new NotSupportedException("Trionic 5 binaries have no address table to copy");

        public override string ExportIdc()
        {
            IdaProIdcFile.create(FileName, Info, File);
            return Path.Combine(Path.GetDirectoryName(FileName) ?? "", Path.GetFileNameWithoutExtension(FileName) + ".idc");
        }

        public override void Disassemble(string output, bool full)
        {
            var disasm = new Disassembler();
            if (full)
            {
                disasm.DisassembleFile(true, FileLength + 0x40000, FileName, output, 0, FileLength, Symbols);
                return;
            }
            disasm.DisassembleFile(File, FileName, output, Symbols);
        }

        public override List<(string Name, long Address)> InterruptVectors()
        {
            long[] addresses = File.GetVectorAddresses(FileName);
            return addresses.Select((a, i) => ("Vector " + i, a)).ToList();
        }
    }
}
