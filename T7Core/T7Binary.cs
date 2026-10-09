using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommonSuite;
using NLog;
using TrionicCANLib.Checksum;
using TrionicCANLib.Firmware;

namespace T7
{
    /// <summary>
    /// An opened T7 binary: its symbols and how frmMain read maps and axes from it (TryToOpenFileUsingClass,
    /// GetX/YaxisValues, GetTableMatrixWitdhByName, isSixteenBitTable, GetMapCorrectionFactor, GetSymbolAddress with the
    /// open-software SRAM mapping, TryToAddOpenLoopTables), lifted from frmMain.cs.
    /// </summary>
    public class T7Binary : SuiteBinary
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public string SoftwareVersion { get; }
        public int SramOffset { get; }
        public bool IsBioPower { get; }
        public int Language { get; }

        /// <summary>Settings → Auto fix footer, for the checksum updates of the windows' writes (T7Core's own take it as a parameter).</summary>
        public bool AutoFixFooter { get; set; }

        public override int FileLength => (int)FileT7.Length;

        private T7Binary(string fileName, SymbolCollection symbols, string softwareVersion, int sramOffset, int language) : base(fileName, symbols)
        {
            SoftwareVersion = softwareVersion;
            SramOffset = sramOffset;
            Language = language;
            IsSoftwareOpen = Trionic7File.IsSoftwareOpen(symbols);
            IsBioPower = Find("TorqueCal.M_EngMaxE85Tab") != null;
        }

        /// <summary>frmMain.ValidateFile: 0x80000 bytes starting FF FF EF FC.</summary>
        public static bool IsValidFile(string fileName)
        {
            var fi = new FileInfo(fileName);
            if (!fi.Exists || fi.Length != FileT7.Length) return false;
            byte[] ident = Trionic7File.readdatafromfile(fileName, 0, 4);
            return ident[0] == 0xFF && ident[1] == 0xFF && ident[2] == 0xEF && ident[3] == 0xFC;
        }

        /// <summary>A file to read and write bytes of, without parsing its symbols (rebuild, transfer targets).</summary>
        public static T7Binary OpenRaw(string fileName) => new(fileName, new SymbolCollection(), "", 0, 0);

        public override SuiteBinary RawFile(string fileName) => new T7Binary(fileName, new SymbolCollection(), "", 0, 0) { AutoFixFooter = AutoFixFooter };

        /// <summary>frmMain.TryToOpenFileUsingClass for the working file.</summary>
        public static T7Binary Open(string fileName, int language, bool autoFixFooter)
        {
            string softwareVersion = "";
            int sramOffset = 0;
            try
            {
                var header = new T7FileHeader();
                if (header.init(fileName, autoFixFooter))
                {
                    softwareVersion = header.getSoftwareVersion();
                    sramOffset = header.getSramOffset();
                }
            }
            catch (Exception E)
            {
                logger.Debug(E);
            }
            var file = new Trionic7File();
            SymbolCollection symbols = file.ExtractFile(fileName, language, softwareVersion);
            if (sramOffset == 0) sramOffset = file.SramOffsetForOpenFile;
            var binary = new T7Binary(fileName, symbols, softwareVersion, sramOffset, language) { AutoFixFooter = autoFixFooter };

            // BioPower bins carry the E85 fuel map under the start map's name
            if (binary.IsBioPower)
            {
                foreach (SymbolHelper sh in symbols)
                {
                    if (sh.Varname == "BFuelCal.StartMap")
                    {
                        sh.Varname = "BFuelCal.E85Map";
                        sh.Description = SymbolTranslator.ToHelpText(sh.Varname, language);
                    }
                    if (sh.Userdescription == "BFuelCal.StartMap")
                    {
                        sh.Userdescription = "BFuelCal.E85Map";
                        sh.Description = SymbolTranslator.ToHelpText(sh.Userdescription, language);
                    }
                }
            }
            return binary;
        }

        public override bool Has(string symbolname) => FindAny(symbolname) != null;

        public override string Describe(string symbolname) => SymbolTranslator.ToHelpText(symbolname, Language);

        public override bool ImportXmlSymbols(string file) => Trionic7File.TryToLoadAdditionalXMLSymbols(file, Symbols, Language);

        /// <summary>T7Suite's compare name: the name, or the user description of a "Symbolnumber N"; never SymbolNames or LocalID.</summary>
        public override string CompareName(SymbolHelper sh) =>
            sh.Varname is "SymbolNames" or "LocalID" || sh.Userdescription is "SymbolNames" or "LocalID" ? ""
            : sh.Varname.StartsWith("Symbolnumber") ? sh.Userdescription : sh.Varname;

        public override bool IsCalibration(string name) => base.IsCalibration(name) || name.StartsWith("DisplAdap.");

        // GetOpenFileOffset: the header's SRAM offset, or the usual one
        private int OpenFileOffset => SramOffset > 0 ? SramOffset : 0xEFFC04;

        public override int PackageAddressOffset => IsSoftwareOpen ? OpenFileOffset : 0;

        /// <summary>Flash address; in open software, calibration symbols listed at their SRAM address map back into the file.</summary>
        public override long SymbolAddress(string symbolname) => Find(symbolname) is { } sh ? AddressOf(sh) : 0;

        public override long AddressOf(SymbolHelper sh)
        {
            if (IsSoftwareOpen && IsCalibration(sh.SmartVarname) && sh.Length < 0x400 && sh.Flash_start_address > FileLength)
                return sh.Flash_start_address - OpenFileOffset;
            return sh.Flash_start_address;
        }

        /// <summary>The symbol's bytes at its (open-software mapped) address.</summary>
        public byte[] ReadValue(SymbolHelper sh) => Read((int)AddressOf(sh), sh.Length);

        public override byte[] Read(int address, int length) => Trionic7File.readdatafromfile(FileName, address, length);

        /// <summary>Where a map's data sits in the file (StartTableViewer's Map_address), -1 if it only lives in SRAM.</summary>
        public override int FileAddress(SymbolHelper sh)
        {
            int address = (int)sh.Flash_start_address;
            if (address == 0) return -1;
            if (address < 0x0F00000) return address;
            return IsSoftwareOpen ? address - OpenFileOffset : -1;
        }

        /// <summary>
        /// tabdet_onSymbolSave: savedatatobinary (only inside the 0x80000 file, a transaction entry when a project log is
        /// given) and then the checksum update. Throws when the file can't be written (read-only).
        /// </summary>
        public void WriteSymbol(int address, byte[] data, bool autoFixFooter, TrionicTransactionLog log = null, string note = "")
        {
            WriteData(address, data, log, note);
            UpdateChecksum(autoFixFooter);
        }

        /// <summary>
        /// ChecksumT7.UpdateChecksum until the file verifies. One pass computes the FB checksum before it writes the new FW
        /// checksum, which can lie inside the FB range, so a write that changes the FW checksum leaves FB stale; T7Suite never
        /// noticed because it updated after every single write. Throws if it still doesn't verify.
        /// </summary>
        public override void UpdateChecksum() => UpdateChecksum(AutoFixFooter);

        public void UpdateChecksum(bool autoFixFooter)
        {
            for (int pass = 0; pass < 3; pass++)
            {
                ChecksumT7.UpdateChecksum(FileName, autoFixFooter);
                if (ChecksumT7.VerifyChecksum(FileName, false, autoFixFooter, (_, _, _) => false) == ChecksumResult.Ok) return;
            }
            throw new InvalidOperationException($"The checksum of {Path.GetFileName(FileName)} does not verify after updating it.");
        }

        private byte[] ReadAxis(int address, int length) =>
            address < 0x0F00000 || !IsSoftwareOpen ? Read(address, length) : Read(address - SramOffset, length);

        public override (string xAxis, string yAxis, string xDescr, string yDescr, string zDescr) AxisSymbols(string symbolname)
        {
            new SymbolAxesTranslator().GetAxisSymbols(symbolname, out string x, out string y, out string xd, out string yd, out string zd);
            return (x, y, xd, yd, zd);
        }

        public override int[] GetXaxisValues(string symbolname)
        {
            int[] retval = [0];
            var (x_axis, _, _, _, _) = AxisSymbols(symbolname);
            int xaxislength = 0, xaxisaddress = 0;
            if (x_axis != "")
            {
                xaxislength = SymbolLength(x_axis);
                xaxisaddress = (int)SymbolAddress(x_axis);
            }
            double multiplier = GetMapCorrectionFactor(x_axis);
            if (xaxislength > 0)
            {
                byte[] axisdata = ReadAxis(xaxisaddress, xaxislength);
                retval = new int[xaxislength / 2];
                int offset = 0;
                for (int i = 0; i + 1 < xaxislength; i += 2)
                {
                    int value = axisdata[i] * 256 + axisdata[i + 1];
                    if (symbolname == "BstKnkCal.MaxAirmass" || symbolname == "BstKnkCal.MaxAirmassAu")
                    {
                        if (value > 32000) value = -(65536 - value);
                    }
                    retval[offset++] = (int)(value * multiplier);
                }
            }
            return retval;
        }

        public override int[] GetYaxisValues(string symbolname)
        {
            int[] retval = new int[SymbolLength(symbolname)];
            var (_, y_axis, _, _, _) = AxisSymbols(symbolname);
            int yaxislength = 0, yaxisaddress = 0;
            if (y_axis != "")
            {
                yaxislength = SymbolLength(y_axis);
                yaxisaddress = (int)SymbolAddress(y_axis);
            }
            double multiplier = GetMapCorrectionFactor(y_axis);
            int length = SymbolLength(symbolname);
            if (symbolname == "TorqueCal.M_ManGearLim" || symbolname == "TorqueCal.M_CabGearLim")
                return length == 14 ? [-1, 1, 2, 3, 4, 5, 6] : [-1, 1, 2, 3, 4, 5];
            if (symbolname == "GearCal.Ratio" || symbolname == "GearCal" || symbolname == "GearCal.Range")
                return length == 12 ? [1, 2, 3, 4, 5, 6] : [1, 2, 3, 4, 5];
            if (symbolname == "BstMetCal.BoostMeter")
                return [0, 1, 2, 3, 4, 5];
            if (yaxislength > 0)
            {
                byte[] axisdata = ReadAxis(yaxisaddress, yaxislength);
                retval = new int[yaxislength / 2];
                int offset = 0;
                for (int i = 0; i + 1 < yaxislength; i += 2)
                {
                    int value = (int)((axisdata[i] * 256 + axisdata[i + 1]) * multiplier);
                    if (value > 0x8000) value = -(0x10000 - value);
                    retval[offset++] = value;
                }
            }
            return retval;
        }

        /// <summary>GetTableMatrixWitdhByName: the number of columns, by name or else by byte length.</summary>
        public override int TableWidth(string symbolname)
        {
            switch (symbolname)
            {
                case "TorqueCal.M_NominalMap": case "TorqueCal.M_IgnInflTorqMap": case "TorqueCal.fi_IgnLimMap": case "IgnKnkCal.AdapTimer":
                case "IgnNormCal2Type": case "KnkSoundRedCalType": case "MissfCal.DetLevLowLim": case "MissfCal.DetectLevel":
                case "KnkFuelCal.EnrichmentMap": case "KnkFuelCalType": case "BFuelCal.Map": case "BFuelCal.GasMap":
                case "MyrtilosCal.Fuel_GasMap": case "BFuelCal.StartMap": case "BFuelCal.E85Map": case "BFuelCal2.Map":
                case "BFuelCal2.StartMap": case "MissfAdap.MissfCntMap":
                    return 18;
                case "TorqueCal.X_AccPedalMap": case "KnkFuelCal.fi_MapMaxOff": case "KnkDetCal.RefFactorMap":
                    return 16;
                case "AftSt2ExtraCal.EnrFacMap": case "AftSt1ExtraCal.EnrFacMap": case "StartCal.HighAltFacMap":
                    return 15;
                case "AirComp.LimPresComp": case "TCompCal.EnrFacMap": case "TCompCal.EnrFacE85Map": case "TCompCal.EnrFacAutMap":
                case "StartCal.ScaleFacRpmE85Map": case "StartCal.ScaleFacRpmMap":
                    return 8;
                case "AftSt2ExtraCal.EnrMapE85":
                    return 7;
                case "HotStCal2.RestartMap":
                    return 6;
                case "IgnTempCal.AirMap": case "IgnTempCal.EngMap": case "MissfCal.DetectLoadLevel":
                    return 5;
                case "TorqueCal.fi_IgnMinTab": case "BoostCal.p_DiffILimMap": case "MissfCal.outOfLimDelayMAT": case "KnkAdaptCal.WeightMap2":
                    return 4;
                case "KnkAdaptCal.MaxRef":
                    return 3;
                case "TorqueCal.M_PumpLossMap": case "SwitchCal.A_AmbPresMap":
                    return 2;
            }
            return SymbolLength(symbolname) switch
            {
                576 => 18, 512 => 16, 336 => 12, 288 => 9, 256 => 8, 242 => 11, 224 => 8, 200 => 10, 198 => 9, 192 => 8,
                128 => 8, 120 => 5, 100 => 10, 98 => 7, 80 => 5, 60 => 5, 50 => 5, 96 => 6, 64 => 4, 160 => 10, 72 => 9,
                _ => 1,
            };
        }

        public override bool IsSixteenBitTable(string symbolname)
        {
            switch (symbolname)
            {
                case "KnkDetCal.RefFactorMap": case "BFuelCal.Map": case "BFuelCal.GasMap": case "MyrtilosCal.Fuel_GasMap":
                case "BFuelCal.StartMap": case "BFuelCal.E85Map": case "BFuelCal2.Map": case "BFuelCal2.StartMap":
                case "TorqueCal.M_IgnInflTorqMap": case "TCompCal.EnrFacMap": case "TCompCal.EnrFacAutMap": case "TCompCal.EnrFacE85Map":
                case "AftSt2ExtraCal.EnrFacMap": case "AftSt1ExtraCal.EnrFacMap": case "StartCal.HighAltFacMap": case "MissfAdap.MissfCntMap":
                case "WriteProtectedECU": case "Data_name": case "MAFCal.ConstT_EngineTab": case "MAFCal.ConstT_AirInlTab":
                    return false;
            }
            return SymbolLength(symbolname) % 2 == 0;
        }

        /// <summary>"Resolution is X" from the symbol's help text, a few hard-coded ones, else 1.</summary>
        public override double GetMapCorrectionFactor(string symbolname) => CorrectionFactor(symbolname, Language);

        public static double CorrectionFactor(string symbolname, int language)
        {
            double returnvalue = 1;
            try
            {
                string text = SymbolTranslator.ToHelpText(symbolname, language);
                int idx = text.IndexOf("Resolution is", StringComparison.Ordinal);
                if (idx >= 0)
                {
                    string value = text.Substring(idx + 14).Trim();
                    if (value.Contains(' ')) value = value.Substring(0, value.IndexOf(' '));
                    returnvalue = ToDouble(ClearToNumber(value));
                }
            }
            catch (Exception E)
            {
                logger.Debug(E.Message);
            }
            if (returnvalue == 0)
            {
                returnvalue = symbolname switch
                {
                    "KnkSoundRedCal.fi_OffsMap" or "IgnE85Cal.fi_AbsMap" or "IgnNormCal.GasMap" or "BstKnkCal.OffsetXSP" => 0.1,
                    "MAFCal.cd_ThrottleMap" => 0.0009765625,
                    "HotStCal2.RestartMap" => 0.001,
                    _ => 1,
                };
            }
            return returnvalue;
        }

        // digits with at most one '.' or ',', up to the first other character
        private static string ClearToNumber(string value)
        {
            string retval = "";
            bool dotseen = false;
            foreach (char c in value)
            {
                bool isdot = c == '.' || c == ',';
                if (!char.IsAsciiDigit(c) && !isdot) break;
                if (isdot)
                {
                    if (!dotseen) retval += '.';
                    dotseen = true;
                }
                else
                {
                    retval += c;
                }
            }
            return retval;
        }

        private static double ToDouble(string v) =>
            double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d) ? d : 0;

        /// <summary>TryToAddOpenLoopTables: LambdaCal.MaxLoadNormTab, or the E85 one for the E85 maps.</summary>
        public override byte[] OpenLoopTable(string mapname)
        {
            string table = mapname == "IgnE85Cal.fi_AbsMap" || mapname == "BFuelCal.E85Map" ? "LambdaCal.MaxLoadE85Tab" : "LambdaCal.MaxLoadNormTab";
            int length = SymbolLength(table);
            return length == 0 ? null : Read((int)SymbolAddress(table), length);
        }

        // never corrects; a null "should I update?" callback would crash the library on a mismatch
        public override ChecksumResult VerifyChecksum() => ChecksumT7.VerifyChecksum(FileName, false, false, (_, _, _) => false);

        /// <summary>
        /// The Tuning page's map buttons in its group and button order, with DynamicTuningMenu's rules (BioPower names and E85
        /// maps, B308 second maps, gas maps, boost control and cab gear limit only when the bin has them).
        /// </summary>
        public override List<MapShortcut> QuickMaps()
        {
            bool bio = IsBioPower, b308 = Has("IgnNormCal2.Map");
            var m = new List<MapShortcut>
            {
                new("Fuel", bio ? "Petrol VE Map" : "VE map", "BFuelCal.Map"),
                new("Fuel", bio ? "E85 VE Map" : "Startup VE map", bio ? "BFuelCal.E85Map" : "BFuelCal.StartMap"),
                new("Fuel", "Injector constant", "InjCorrCal.InjectorConst"),
            };
            if (b308)
            {
                m.Add(new("Fuel", "VE map2", "BFuelCal2.Map"));
                m.Add(new("Fuel", "Startup VE map2", "BFuelCal2.StartMap"));
            }
            if (Has("MyrtilosCal.Fuel_GasMap")) m.Add(new("Fuel", "Gas VE map", "MyrtilosCal.Fuel_GasMap"));
            else if (Has("BFuelCal.GasMap")) m.Add(new("Fuel", "Gas VE map", "BFuelCal.GasMap"));

            m.Add(new("Ignition", "Ignition map", "IgnNormCal.Map"));
            if (b308) m.Add(new("Ignition", "Ignition map2", "IgnNormCal2.Map"));
            if (bio) m.Add(new("Ignition", "Ignition for E85", "IgnE85Cal.fi_AbsMap"));
            if (Has("IgnNormCal.GasMap")) m.Add(new("Ignition", "Ignition for gas", "IgnNormCal.GasMap"));
            m.Add(new("Ignition", "Knock pull map", "IgnKnkCal.IndexMap"));
            m.Add(new("Ignition", "Max knock pull", "KnkFuelCal.fi_MapMaxOff"));

            m.Add(new("Airmass request", "Pedal request map", "PedalMapCal.m_RequestMap"));
            m.Add(new("Airmass request", "Air/torque calibration", "TorqueCal.m_AirTorqMap"));
            m.Add(new("Airmass request", "Nom. torque map", "TorqueCal.M_NominalMap"));
            m.Add(new("Airmass request", "Pedal request airmass (Y)", "TorqueCal.m_PedYSP"));
            m.Add(new("Airmass request", "Air/torque (X)", "TorqueCal.M_EngXSP"));
            m.Add(new("Airmass request", "Nom. torque map (X)", "TorqueCal.m_AirXSP"));

            if (Has("BoostCal.RegMap"))
            {
                m.Add(new("Boost control", "Boost calibr.", "BoostCal.RegMap"));
                m.Add(new("Boost control", "P factors", "BoostCal.PMap"));
                m.Add(new("Boost control", "I factors", "BoostCal.IMap"));
                m.Add(new("Boost control", "D factors", "BoostCal.DMap"));
            }

            m.Add(new("Knock", "Knock enrichment", "KnkFuelCal.EnrichmentMap"));
            m.Add(new("Knock", "Knock sensitivity", "KnkDetCal.RefFactorMap"));

            m.Add(new("Limiters", "Airmass (M)", "BstKnkCal.MaxAirmass"));
            m.Add(new("Limiters", "Airmass (A)", "BstKnkCal.MaxAirmassAu"));
            m.Add(new("Limiters", "RPM limiter", "MaxSpdCal.n_EngLimAir"));
            m.Add(new("Limiters", "Engine trq (M)", "TorqueCal.M_EngMaxTab"));
            m.Add(new("Limiters", "Engine trq (A)", "TorqueCal.M_EngMaxAutTab"));
            m.Add(new("Limiters", "Fuel cut", "FCutCal.m_AirInletLimit"));
            if (bio) m.Add(new("Limiters", "Engine trq for E85", "TorqueCal.M_EngMaxE85Tab"));
            if (bio && Has("TorqueCal.M_EngMaxE85TabAut")) m.Add(new("Limiters", "Engine trq for E85 (A)", "TorqueCal.M_EngMaxE85TabAut"));
            m.Add(new("Limiters", "Speed limiter", "MaxVehicCal.v_MaxSpeed"));
            m.Add(new("Limiters", "Gear trq (M)", "TorqueCal.M_ManGearLim"));
            m.Add(new("Limiters", "Gear trq (5th)", "TorqueCal.M_5GearLimTab"));
            if (Has("TorqueCal.M_CabGearLim")) m.Add(new("Limiters", "Gear trq (cab)", "TorqueCal.M_CabGearLim"));
            m.Add(new("Limiters", "Overboost", "TorqueCal.M_OverBoostTab"));
            return m;
        }

        /// <summary>Export fixed tuning package: the maps a stage tune touches.</summary>
        public override IReadOnlyList<string> FixedPackageSymbols { get; } =
        [
            "LimEngCal.TurboSpeedTab", "LimEngCal.p_AirSP", "AirCtrlCal.m_MaxAirTab", "TempLimPosCal.Airmass", "BoostCal.RegMap",
            "BoostCal.SetLoadXSP", "BoostCal.n_EngSP", "PedalMapCal.m_RequestMap", "PedalMapCal.n_EngineMap", "PedalMapCal.X_PedalMap",
            "BstKnkCal.MaxAirmass", "BstKnkCal.OffsetXSP", "BstKnkCal.n_EngYSP", "BstKnkCal.MaxAirmassAu", "TorqueCal.M_EngMaxAutTab",
            "TorqueCal.M_EngMaxTab", "TorqueCal.M_EngMaxE85Tab", "TorqueCal.M_ManGearLim", "TorqueCal.M_CabGearLim", "TorqueCal.n_Eng5GearSP",
            "TorqueCal.M_5GearLimTab", "TorqueCal.M_NominalMap", "TorqueCal.m_AirXSP", "TorqueCal.n_EngYSP", "TorqueCal.m_AirTorqMap",
            "TorqueCal.M_EngXSP", "TorqueCal.m_PedYSP", "FCutCal.m_AirInletLimit", "BoosDiagCal.m_FaultDiff", "BoosDiagCal.ErrMaxMReq",
            "BFuelCal.Map", "BFuelCal.StartMap", "BFuelCal.E85Map", "MyrtilosCal.Fuel_GasMap", "BFuelCal.GasMap", "BFuelCal.AirXSP",
            "BFuelCal.RpmYSP", "InjCorrCal.BattCorrSP", "InjCorrCal.BattCorrTab", "InjCorrCal.InjectorConst", "IgnNormCal.Map",
            "IgnE85Cal.fi_AbsMap", "IgnNormCal.GasMap", "IgnNormCal.m_AirXSP", "IgnNormCal.n_EngYSP", "IgnKnkCal.IndexMap",
            "KnkFuelCal.fi_MapMaxOff", "KnkFuelCal.m_AirXSP", "BoostCal.PMap", "BoostCal.IMap", "BoostCal.DMap", "BoostCal.PIDXSP",
            "BoostCal.PIDYSP", "TorqueCal.M_OverBoostTab", "TorqueCal.n_EngYSP", "KnkFuelCal.EnrichmentMap", "IgnKnkCal.m_AirXSP",
            "IgnKnkCal.n_EngYSP", "KnkDetCal.RefFactorMap", "KnkDetCal.m_AirXSP", "KnkDetCal.n_EngYSP", "MaxSpdCal.T_EngineSP",
            "MaxSpdCal.n_EngLimAir", "MaxVehicCal.v_MaxSpeed",
        ];

        public override string ExportIdc() => BinaryTools.ExportIdc(this);

        public override int AddressTableStart(string file) => BinaryTools.AddressTableOffset(File.ReadAllBytes(file));

        public override void CopyAddressTable(string target) => BinaryTools.CopyAddressTable(FileName, target, AutoFixFooter);

        /// <summary>"Compare to original file": the stock bin with the part number in Binaries next to the executable.</summary>
        public string OriginalFile()
        {
            var header = new T7FileHeader();
            header.init(FileName, false);
            string dir = Path.Combine(AppContext.BaseDirectory, "Binaries");
            if (!Directory.Exists(dir)) return null;
            string[] files = Directory.GetFiles(dir, header.getPartNumber().Trim() + ".bin", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive });
            return files.Length == 1 ? files[0] : null;
        }
    }
}
