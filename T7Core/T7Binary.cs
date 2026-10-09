using System;
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

        /// <summary>By name or user description, as frmMain's feature checks matched symbols.</summary>
        public SymbolHelper FindAny(string symbolname) =>
            Symbols.Cast<SymbolHelper>().FirstOrDefault(sh => sh.Varname == symbolname || sh.Userdescription == symbolname);

        public override bool Has(string symbolname) => FindAny(symbolname) != null;

        // GetOpenFileOffset: the header's SRAM offset, or the usual one
        private int OpenFileOffset => SramOffset > 0 ? SramOffset : 0xEFFC04;

        private static bool IsSymbolCalibration(string symbolname) =>
            symbolname.Contains("Cal.") || symbolname.Contains("Cal1.") || symbolname.Contains("Cal2.") || symbolname.Contains("Cal3.")
            || symbolname.Contains("Cal4.") || symbolname.StartsWith("X_Acc") || symbolname.StartsWith("DisplAdap.");

        /// <summary>Flash address; in open software, calibration symbols listed at their SRAM address map back into the file.</summary>
        public override long SymbolAddress(string symbolname) => Find(symbolname) is { } sh ? AddressOf(sh) : 0;

        public long AddressOf(SymbolHelper sh)
        {
            if (IsSoftwareOpen && IsSymbolCalibration(sh.SmartVarname) && sh.Length < 0x400 && sh.Flash_start_address > FileLength)
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
    }
}
