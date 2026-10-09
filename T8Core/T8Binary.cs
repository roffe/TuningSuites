using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommonSuite;
using NLog;
using TrionicCANLib.Checksum;
using TrionicCANLib.Firmware;

namespace T8SuitePro
{
    /// <summary>
    /// An opened T8 binary: its symbols and how Form1 read maps and axes from it (TryToOpenFile, GetSymbolAddress,
    /// GetTableMatrixWitdhByName, isSixteenBitTable, GetMapCorrectionFactor, GetX/YaxisValues, savedatatobinary, UpdateChecksum),
    /// lifted from Form1.cs. SRAM addresses were mapped to the file once at open (Trionic8File.TranslateAddressOffsets), so a
    /// symbol at 0x100000 or above only lives in SRAM. Lookups go by SmartVarname, as Form1's did.
    /// </summary>
    public class T8Binary : SuiteBinary
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public T8Header Header { get; }
        public int AddressOffset { get; }
        public int SecondaryOffset { get; }

        /// <summary>The PID table (the PID editor), null when none was found.</summary>
        public PidCollection Pids { get; }

        /// <summary>The TEM table (the TEM editor), null when none was found; none of the stock bins has one.</summary>
        public PidCollection Tems { get; }

        /// <summary>A symbol list (XML) gave the names, so map detection didn't run.</summary>
        public bool SymbolListLoaded { get; }

        public string SoftwareVersion => Header.SoftwareVersion;

        private const int FileSize = (int)FileT8.Length;

        public override int FileLength => FileSize;

        private T8Binary(string fileName, SymbolCollection symbols, T8Header header, Trionic8File file, PidCollection pids, PidCollection tems,
            bool symbolListLoaded) : base(fileName, symbols)
        {
            Header = header;
            IsSoftwareOpen = file.IsSoftwareOpen;
            AddressOffset = file.AddressOffset;
            SecondaryOffset = file.SecondaryOffset;
            Pids = pids;
            Tems = tems;
            SymbolListLoaded = symbolListLoaded;
        }

        /// <summary>
        /// Trionic8File.ValidateTrionic8File: 0x100000 bytes starting 00 10 0C 00 or 00 00 0C 00. As in T8Suite a wrong file says why
        /// ("File has incorrect length" / "File does not seem to be a Trionic 8 file"), through UserPrompt.
        /// </summary>
        public static bool IsValidFile(string fileName) => Trionic8File.ValidateTrionic8File(fileName);

        /// <summary>
        /// Form1.TryToOpenFile: the symbol table, then a symbol list (XML) from symbolListFolder (the program's folder) or next to the
        /// bin, else map detection when asked for (Settings → Auto mapdetection active). Failures are logged and leave what was read.
        /// </summary>
        public static T8Binary Open(string fileName, bool mapDetection, string symbolListFolder = null)
        {
            var file = new Trionic8File();
            SymbolCollection symbols = null;
            PidCollection pids = null, tems = null;
            bool loaded = false;
            try
            {
                file.TryToExtractPackedBinary(fileName, out symbols, out pids, out tems);
                symbols ??= new SymbolCollection();
                // symbol names the user entered earlier
                loaded = Trionic8File.TryToLoadAdditionalBinSymbols(fileName, symbols, symbolListFolder);
            }
            catch (Exception E)
            {
                logger.Debug("TryOpenFile failed: " + fileName + " err: " + E.Message);
            }
            symbols ??= new SymbolCollection();
            try
            {
                if (mapDetection && !loaded) new SymbolFiller().CheckAndFillCollection(symbols);
            }
            catch (Exception E)
            {
                logger.Debug(E.Message);
            }
            var header = new T8Header();
            header.init(fileName);
            return new T8Binary(fileName, symbols, header, file, pids, tems, loaded);
        }

        // a project's rebuild: bytes only, no symbols or header
        private T8Binary(string fileName) : base(fileName, new SymbolCollection()) => Header = new T8Header();

        public override SuiteBinary RawFile(string fileName) => new T8Binary(fileName);

        /// <summary>Form1.GetSymbolAddress: the file address, 0 for a symbol that only lives in SRAM (or isn't there).</summary>
        public override long SymbolAddress(string symbolname) => Find(symbolname) is { } sh && sh.Flash_start_address < FileSize ? sh.Flash_start_address : 0;

        /// <summary>Where a map's data sits in the file (StartTableViewer's Map_address), -1 if it only lives in SRAM.</summary>
        public override int FileAddress(SymbolHelper sh) => sh.Flash_start_address is > 0 and < FileSize ? (int)sh.Flash_start_address : -1;

        /// <summary>Form1.readdatafromfile: a length of 0 gives one byte.</summary>
        public override byte[] Read(int address, int length) => length <= 0 ? new byte[1] : Trionic8File.readdatafromfile(FileName, address, length);

        /// <summary>
        /// UpdateChecksum(file, true): ChecksumT8 corrects both layers in one pass (the PI area it writes to lies outside them).
        /// Throws when the file can't be corrected, which T8Suite's status bar left unsaid. A map save always corrects it, as
        /// T8Suite did whatever AutoChecksum said.
        /// </summary>
        public override void UpdateChecksum()
        {
            ChecksumResult result = ChecksumT8.VerifyChecksum(FileName, true, (_, _, _) => true);
            if (result != ChecksumResult.Ok || VerifyChecksum() != ChecksumResult.Ok)
                throw new InvalidOperationException($"The checksum of {Path.GetFileName(FileName)} could not be updated ({result}).");
        }

        // never corrects; a null "should I update?" callback would crash the library on a mismatch
        public override ChecksumResult VerifyChecksum() => ChecksumT8.VerifyChecksum(FileName, false, (_, _, _) => false);

        /// <summary>
        /// The axis symbols and units (GetAxisDescriptions / StartTableViewer): SymbolDictionary's, with the duplicate's x axis when
        /// the listed one isn't in the file. The units are those of the listed axes, as T8Suite showed them.
        /// </summary>
        public override (string xAxis, string yAxis, string xDescr, string yDescr, string zDescr) AxisSymbols(string symbolname)
        {
            new SymbolAxesTranslator().GetAxisSymbols(symbolname, out string x, out string y, out string xd, out string yd, out string zd);
            if (SymbolDictionary.doesDuplicateExist(symbolname, out _, out string alt) && !Has(x)) x = alt;
            return (x, y, xd, yd, zd);
        }

        /// <summary>
        /// Form1.GetXaxisValues: raw (no correction factor), unsigned; "N : v1 v2 …" axes from the dictionary as they are. A map
        /// without a y axis gets no x values either (SymbolAxesTranslator returns false then), as in T8Suite.
        /// </summary>
        public override int[] GetXaxisValues(string symbolname)
        {
            int[] retval = new int[0];
            int xaxisaddress = 0;
            int xaxislength = 0;
            bool issixteenbit = true;
            if (symbolname == "EvapDiagCal.LeakFacTest2MAT") issixteenbit = false;
            if (symbolname == "EvapDiagCal.LeakFacTest1MAT") issixteenbit = false;
            if (new SymbolAxesTranslator().GetAxisSymbols(symbolname, out string x_axis, out _, out _, out _, out _))
            {
                if (x_axis != "")
                {
                    // Check if there are duplicates
                    if (SymbolDictionary.doesDuplicateExist(symbolname, out _, out string alt_axis))
                    {
                        // Check if the current loaded axis exist in the file
                        if (!Has(x_axis)) x_axis = alt_axis;
                    }
                    if (char.IsDigit(x_axis[0]) && StaticAxis(x_axis) is { } values) return values;
                    xaxislength = SymbolLength(x_axis);
                    xaxisaddress = (int)SymbolAddress(x_axis);
                }
            }
            if (xaxislength > 0)
            {
                byte[] axisdata = Read(xaxisaddress, xaxislength);
                retval = new int[issixteenbit ? xaxislength / 2 : xaxislength];
                int offset = 0;
                for (int i = 0; i < xaxislength; i++)
                {
                    if (issixteenbit)
                    {
                        int value = axisdata[i] * 256 + axisdata[++i];
                        retval[offset++] = value;
                    }
                    else
                    {
                        retval[offset++] = axisdata[i];
                    }
                }
            }
            return retval;
        }

        /// <summary>Form1.GetYaxisValues: raw, above 0x8000 negative; "N : v1 v2 …" axes from the dictionary as they are.</summary>
        public override int[] GetYaxisValues(string symbolname)
        {
            int[] retval = new int[0];
            int yaxisaddress = 0;
            int yaxislength = 0;
            bool issixteenbit = symbolname != "FuelDynCal.FuelModFacTab";
            if (new SymbolAxesTranslator().GetAxisSymbols(symbolname, out _, out string y_axis, out _, out _, out _))
            {
                if (y_axis != "")
                {
                    if (char.IsDigit(y_axis[0]) && StaticAxis(y_axis) is { } values) return values;
                    yaxislength = SymbolLength(y_axis);
                    yaxisaddress = (int)SymbolAddress(y_axis);
                }
            }
            if (yaxislength > 0)
            {
                byte[] axisdata = Read(yaxisaddress, yaxislength);
                retval = new int[issixteenbit ? yaxislength / 2 : yaxislength];
                int offset = 0;
                for (int i = 0; i < yaxislength; i++)
                {
                    if (issixteenbit)
                    {
                        int value = axisdata[i] * 256 + axisdata[++i];
                        if (value > 0x8000) value = -(0x10000 - value);
                        retval[offset++] = value;
                    }
                    else
                    {
                        retval[offset++] = axisdata[i];
                    }
                }
            }
            return retval;
        }

        // "8 : 0 1 2 3 4 5 6 7": the count, then the values (extra values ignored); null when it isn't that form. A label that isn't
        // a number counts as its position: EngTipLimCal.X_Koeff's "6: A B C D E F" made T8Suite's viewer throw and not open
        private static int[] StaticAxis(string axis)
        {
            string[] tmp = axis.Split(':');
            if (tmp.Length != 2) return null;
            int len = Convert.ToInt32(tmp[0]);
            int[] retval = new int[len];
            int index = 0;
            foreach (string v in tmp[1].Trim().Split(' '))
            {
                if (index < len) retval[index] = int.TryParse(v, out int value) ? value : index;
                index++;
            }
            return retval;
        }

        /// <summary>
        /// The number of columns the viewer shows: GetTableMatrixWitdhByName (by name, else by byte length; support point and
        /// table symbols one column), then the x axis' length when it has more than one value, as StartTableViewer did.
        /// </summary>
        public override int TableWidth(string symbolname)
        {
            int length = SymbolLength(symbolname);
            int columns = symbolname switch
            {
                "MisfCal.m_LoadLevelMAT" => 5,
                "PedalMapCal.GainFactorMap" => 16,
                "FrictionLoadCal.Trq_RequestT_EngMAP" => 9,
                "CatDiagCal.t_Ph3MaxMAT" => 7,
                _ => length switch
                {
                    576 => 18, 672 => 16, 512 => 16, 504 => 14, 480 => 6, 416 => 16, 384 => 12,
                    336 => symbolname.StartsWith("PurgeCal.") ? 12 : 16,
                    320 => 10, 306 => 9, 288 => 18, 256 => 8, 224 => 8, 220 => 10, 208 => 13, 204 => 6, 200 => 10, 192 => 8, 168 => 7,
                    140 => 7, 130 => 1, 128 => 8, 112 => 14, 100 => 10, 98 => 7, 80 => 8, 60 => 5, 50 => 5, 96 => 6, 64 => 4, 160 => 10,
                    72 => 9, 42 => 7,
                    _ => 1,
                },
            };
            string upper = symbolname.ToUpper();
            if (upper.EndsWith("YSP") || upper.EndsWith("XSP") || upper.EndsWith("TAB")) columns = 1;
            int[] x = GetXaxisValues(symbolname);
            return x.Length > 1 ? x.Length : columns;
        }

        /// <summary>Form1.isSixteenBitTable: 16-bit unless listed, of odd length, or 336 bytes outside PurgeCal.</summary>
        public override bool IsSixteenBitTable(string symbolname)
        {
            switch (symbolname)
            {
                case "KnkDetCal.RefFactorMap": case "BFuelCal.Map": case "BFuelCal.StartMap": case "TorqueCal.M_IgnInflTorqM":
                case "TCompCal.EnrFacMap": case "TCompCal.EnrFacAutMap": case "AftSt2ExtraCal.EnrFacMap": case "AftSt1ExtraCal.EnrFacMap":
                case "StartCal.HighAltFacMap": case "BFuelCal.TempEnrichFacMap": case "BFuelCal.E85TempEnrichFacMap":
                case "BFuelCal.LambdaOneFacMap": case "MAFCal.NormAdjustFacMap": case "CatModCal.TSoakFacMAP": case "StartCal.ScaleFacRpmMap":
                case "ECUIDCal.ApplicationFileName": case "FFFuelCal.TempEnrichFacMAP":
                    return false;
            }
            if (symbolname.StartsWith("FuelDynCal.m_FbetaMap") || symbolname.StartsWith("FuelDynCal.m_FalphaMap")) return false;
            int length = SymbolLength(symbolname);
            return length % 2 != 1 && !(length == 336 && !symbolname.StartsWith("PurgeCal."));
        }

        /// <summary>
        /// Form1.GetMapCorrectionFactor's result: the dictionary's unit factor (1 when unknown). The "Resolution is" parsing and the
        /// hard-coded list before it were overwritten by this line in T8Suite.
        /// </summary>
        public override double GetMapCorrectionFactor(string symbolname) => SymbolDictionary.GetSymbolUnit(symbolname);

        /// <summary>T8Suite gave the viewer its ECU buttons only for symbols with an SRAM address.</summary>
        public override bool InSram(SymbolHelper sh) => sh.Start_address >= 0x100000;

        public override string Describe(string symbolname) => SymbolTranslator.ToDescription(symbolname);

        public override bool ImportXmlSymbols(string file) => Trionic8File.TryToLoadAdditionalXMLSymbols(file, Symbols);

        public override int AddressTableStart(string file) => Trionic8File.AddressTableStart(file);

        public override string ExportIdc() => IdaProIdcFile.create(FileName, Symbols, IsSoftwareOpen);

        /// <summary>
        /// Copy address table to another binary (Form1 13268): the 10-byte records from 17 bytes before the table's start while
        /// their last byte is zero, into the same place of the target, then the target's checksum (as T8Suite).
        /// </summary>
        public override void CopyAddressTable(string target)
        {
            int a = AddressTableStart(FileName), b = AddressTableStart(target);
            if (a <= 17 || b <= 17) throw new InvalidOperationException("No address table found");
            byte[] from = File.ReadAllBytes(FileName), to = File.ReadAllBytes(target);
            for (int s = a - 17, d = b - 17; s + 10 <= from.Length && d + 10 <= to.Length && from[s + 9] == 0; s += 10, d += 10)
                Array.Copy(from, s, to, d, 10);
            File.WriteAllBytes(target, to);
            RawFile(target).UpdateChecksum();
        }

        /// <summary>The viewer shows T8's axes raw; the Excel export applied the axis' dictionary factor.</summary>
        public override double AxisFactor(string axisSymbol) => GetMapCorrectionFactor(axisSymbol);

        /// <summary>Export fixed tuning package (Form1's list).</summary>
        public override IReadOnlyList<string> FixedPackageSymbols { get; } =
        [
            "AirCtrlCal.PRatioMaxTab", "BstKnkCal.MaxAirmass", "BstKnkCal.MaxAirmassAu", "BFuelCal.TempEnrichFacMap", "BFuelCal.E85TempEnrichFacMap",
            "KnkFuelCal.EnrichmentMap", "KnkFuelCal.fi_OffsetEnrichEnable", "KnkFuelCal.fi_MaxOffsetMap", "IgnAbsCal.fi_highOctanMAP",
            "IgnAbsCal.fi_lowOctanMAP", "IgnAbsCal.fi_NormalMAP", "IgnAbsCal.fi_StartMAP", "DNCompCal.SlowDriveRelTAB", "TrqLimCal.Trq_ManGear",
            "TrqLimCal.Trq_MaxEngineManTab1", "TrqLimCal.Trq_MaxEngineAutTab1", "TrqLimCal.Trq_MaxEngineManTab2", "TrqLimCal.Trq_MaxEngineAutTab2",
            "TrqLimCal.Trq_OverBoostTab", "MaxEngSpdCal.n_EngMin", "TrqMastCal.Trq_NominalMap", "TrqMastCal.m_AirTorqMap", "TMCCal.Trq_MaxEngineTab",
            "TMCCal.Trq_MaxEngineLowTab", "InjCorrCal.BattCorrSP", "InjCorrCal.BattCorrTab", "InjCorrCal.InjectorConst",
        ];

        /// <summary>
        /// The Tuning page's map buttons in ribbon order, with DynamicTuningMenu's rules: software before FC (and FC01 open) has
        /// the old calibration's captions and maps, BioPower software (not FA / FC / FE) the E85 limiters, and the gas and jerk
        /// maps show only when the bin has them. T8Suite left the captions of a bin without a software version alone and threw
        /// on the tagged buttons; that counts as old here.
        /// </summary>
        public override List<MapShortcut> QuickMaps()
        {
            string sw = SoftwareVersion.Trim();
            bool old = sw.Length <= 2 || sw[1] < 'C' || sw.StartsWith("FC01_O");
            bool e85 = !old && !(sw.StartsWith("FA") || sw.StartsWith("FC") || sw.StartsWith("FE"));
            var m = new List<MapShortcut>();
            void Add(string group, string caption, string symbol, bool show = true)
            {
                if (show) m.Add(new MapShortcut(group, caption, symbol));
            }
            void IfHas(string group, string caption, string symbol) => Add(group, caption, symbol, Has(symbol));

            const string air = "Airmass controller", trq = "Torque controller", fuel = "Fuel controller", boost = "Boost controller",
                ign = "Ignition controller", pedal = "Pedal controller";
            Add(air, old ? "Max airmass map (manual)" : "Max air Petrol", "BstKnkCal.MaxAirmass");
            Add(air, old ? "Max airmass map (auto)" : "Max air E85", old ? "BstKnkCal.MaxAirmassAu" : "FFAirCal.m_maxAirmass");
            Add(air, "Airmass Fuelcut", "FCutCal.m_AirInletLimit");

            Add(trq, "Nominal torque map", "TrqMastCal.Trq_NominalMap");
            IfHas(trq, "Nominal Gas torque map", "TrqMastCal.Trq_NominalGasMap");
            Add(trq, "Airmass torque map", "TrqMastCal.m_AirTorqMap");
            IfHas(trq, "Airmass Gas torque map", "TrqMastCal.m_AirTorqGasMap");
            Add(trq, "Ambient pressure trq limiter", "TrqLimCal.Trq_CompressorNoiseRedLimMAP");
            Add(trq, "Trq limit in overboost", "TrqLimCal.Trq_OverBoostTab");
            Add(trq, old ? "Trq limit auto 150 hp" : "Trq limit 150hp", old ? "TrqLimCal.Trq_MaxEngineAutTab2" : "TrqLimCal.Trq_MaxEngineTab2");
            Add(trq, old ? "Trq limit auto 175+ hp" : "Trq limit 175/200hp", old ? "TrqLimCal.Trq_MaxEngineAutTab1" : "TrqLimCal.Trq_MaxEngineTab1");
            Add(trq, old ? "Trq limit manual 150 hp" : "Trq limit E85 150hp", old ? "TrqLimCal.Trq_MaxEngineManTab2" : "FFTrqCal.FFTrq_MaxEngineTab2", old || e85);
            Add(trq, old ? "Trq limit manual 175+ hp" : "Trq limit E85 175/200hp", old ? "TrqLimCal.Trq_MaxEngineManTab1" : "FFTrqCal.FFTrq_MaxEngineTab1", old || e85);
            Add(trq, "Manual gear trq limit", "TrqLimCal.Trq_ManGear");
            Add(trq, "FlexFuel torque limit", "FFTrqCal.M_maxMAP", e85);
            Add(trq, "Max torque 150hp", "TMCCal.Trq_MaxEngineLowTab", !old);
            Add(trq, "Max torque 175/200hp", "TMCCal.Trq_MaxEngineTab", !old);
            Add(trq, "RPM limiter", "MaxEngSpdCal.n_EngLimTab");

            Add(fuel, "Fuel correction map", "BFuelCal.LambdaOneFacMap");
            Add(fuel, "Fuel knock map", "KnkFuelCal.EnrichmentMap");
            Add(fuel, "Injection end angle map", "InjAnglCal.Map");
            Add(fuel, "Enrichment Petrol", "BFuelCal.TempEnrichFacMap");
            IfHas(fuel, "Enrichment E85", "FFFuelCal.TempEnrichFacMAP");
            Add(fuel, "Inj. Constant", "InjCorrCal.InjectorConst");
            Add(fuel, "Dead times", "InjCorrCal.BattCorrTab");
            IfHas(fuel, "Enrichment Petrol", "BFuelCal.Lambda1FacMap");
            IfHas(fuel, "Jerk Enrichment Petrol", "BFuelCal.m_AirJerkTab");
            IfHas(fuel, "Jerk Enrichment Fuelmaster", "BFuelCal.JerkEnrichFacTab");

            Add(boost, "P of PID controller", "AirCtrlCal.Ppart_BoostMap");
            Add(boost, "I of PID Controller", "AirCtrlCal.Ipart_BoostMap");
            Add(boost, "D of PID controller", "AirCtrlCal.Dpart_BoostMap");
            Add(boost, "Boost regulation map", "AirCtrlCal.RegMap");

            Add(ign, "Normal ignition map", "IgnAbsCal.fi_NormalMAP");
            Add(ign, "High octane map", "IgnAbsCal.fi_highOctanMAP");
            Add(ign, "Low octane map", "IgnAbsCal.fi_lowOctanMAP");
            IfHas(ign, "Normal Gas ignition map", "IgnAbsCal.fi_NormalGasMAP");
            Add(ign, "MBT ignition map", "IgnAbsCal.fi_IgnMBTMAP");
            IfHas(ign, "MBT Gas ignition map", "IgnAbsCal.fi_IgnMBTGasMAP");
            Add(ign, "Fuel cut ignition map", "IgnAbsCal.fi_FuelCutMAP");
            Add(ign, "Startup map", "IgnAbsCal.fi_StartMAP");

            Add(pedal, "Pedal position map", "TrqMastCal.X_AccPedalMAP");
            Add(pedal, "Torque request map", "PedalMapCal.Trq_RequestMap");

            Add("General", "EGT estimate map", "ExhaustCal.T_Lambda1Map");
            return m;
        }
    }
}
