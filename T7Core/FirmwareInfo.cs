using System;
using System.IO;
using System.Linq;
using CommonSuite;
using TrionicCANLib.Checksum;
using TrionicCANLib.Firmware;

namespace T7
{
    /// <summary>An option frmFirmwareInformation shows as a checkbox: whether the bin has it at all, and its state.</summary>
    public readonly record struct FirmwareOption(bool Available, bool Enabled);

    /// <summary>
    /// What T7Suite's "Firmware information" dialog shows, detected as frmMain did it (the HasBinary* checks,
    /// GetClosedIndicatorOffset, GetProgrammingDateTime, the SID and emission offsets). Read-only; patching comes with editing.
    /// </summary>
    public class FirmwareInfo
    {
        public string CarDescription { get; init; } = "";
        public string SoftwareVersion { get; init; } = "";
        public string PartNumber { get; init; } = "";
        public string ImmobilizerID { get; init; } = "";
        public string ChassisID { get; init; } = "";
        public string SIDDate { get; init; } = "";
        public string OriginalCarType { get; init; } = "";
        public string OriginalEngineType { get; init; } = "";
        /// <summary>Programming stamp at 0x7FD00, null when the bin has none.</summary>
        public DateTime? ProgrammingDate { get; init; }

        public bool ChecksumEnabled { get; init; }
        public bool CompressedSymbolTable { get; init; }
        public bool NoSymbolTable { get; init; }

        public FirmwareOption OpenSIDInfo { get; init; }
        public FirmwareOption TorqueLimiters { get; init; }
        public FirmwareOption CatalystLightOff { get; init; }
        public FirmwareOption SecondLambda { get; init; }
        public FirmwareOption OBDII { get; init; }
        public FirmwareOption BioPower { get; init; }
        public FirmwareOption FastThrottleResponse { get; init; }
        public FirmwareOption ExtraFastThrottleResponse { get; init; }
        public FirmwareOption NoTCS { get; init; }
        public FirmwareOption DisableEmissionLimiting { get; init; }
        public FirmwareOption DisableStartScreen { get; init; }
        public FirmwareOption DisableAdaptionMessages { get; init; }

        public static FirmwareInfo Read(T7Binary bin)
        {
            var header = new T7FileHeader();
            header.init(bin.FileName, false);
            string partnumber = header.getPartNumber();
            ECUInformation ecu = new PartNumberConverter().GetECUInfo(partnumber.Trim(), "");
            string sw = header.getSoftwareVersion();
            bool tipInOut = bin.Symbols.Cast<SymbolHelper>().Any(sh => sh.Varname.StartsWith("EngTipCal.") || sh.Userdescription.StartsWith("EngTipCal."));
            bool lightOff = bin.Symbols.Cast<SymbolHelper>().Any(sh => sh.Varname.StartsWith("IgnLOffCal.") || sh.Userdescription.StartsWith("IgnLOffCal."));
            bool obd2 = bin.Has("OBDCal.OBD2Enabled");
            var (startScreen, adaption) = SidOptions(bin, sw);
            int emission = sw.StartsWith("EU0AF01C") ? bin.Read(0x13837, 1)[0] : -1;

            return new FirmwareInfo
            {
                CarDescription = header.getCarDescription(),
                SoftwareVersion = sw,
                PartNumber = partnumber,
                ImmobilizerID = header.getImmobilizerID(),
                ChassisID = header.getChassisID(),
                SIDDate = header.getSIDDate(),
                OriginalCarType = ecu.Valid ? ecu.Carmodel.ToString() : "",
                OriginalEngineType = ecu.Valid ? ecu.Enginetype.ToString() : "",
                ProgrammingDate = ProgrammingDateTime(bin),
                ChecksumEnabled = ChecksumEnabledIn(bin),
                CompressedSymbolTable = Trionic7File.IsBinaryPackedVersion(bin.FileName, (int)new FileInfo(bin.FileName).Length),
                NoSymbolTable = bin.Symbols.Cast<SymbolHelper>().Count(sh => sh.Varname.StartsWith("Symbolnumber ")) > 10,
                OpenSIDInfo = new(true, IsSidInfoOpen(bin)),
                TorqueLimiters = new(bin.Has("TorqueCal.ST_Loop"), !IsZeroByte(bin, "TorqueCal.ST_Loop", missing: false)),
                CatalystLightOff = new(lightOff, !(IsZero(bin, "IdleCal.ST_EnableLOffRpm") && IsZero(bin, "IgnLOffCal.ST_Enable"))),
                SecondLambda = new(bin.Has("LambdaCal.ST_AdapEnable"), SecondLambdaEnabled(bin)),
                OBDII = new(obd2 || bin.Has("OBDCal.EOBDEnabled") || bin.Has("OBDCal.LOBDEnabled"),
                    obd2 ? !IsZero(bin, "OBDCal.OBD2Enabled") : !IsZero(bin, "OBDCal.EOBDEnabled") || !IsZero(bin, "OBDCal.LOBDEnabled")),
                BioPower = new(false, !IsZeroByte(bin, "E85Cal.ST_Enable", missing: true)),
                FastThrottleResponse = new(tipInOut, tipInOut && IsZero(bin, "EngTipCal.ST_EnableTipin") && IsZero(bin, "EngTipCal.ST_EnableTipou")),
                ExtraFastThrottleResponse = new(tipInOut, tipInOut && IsZero(bin, "EngTipCal.ST_EnableTipin") && IsZero(bin, "EngTipCal.ST_EnableActG2") && IsZero(bin, "EngTipCal.ST_EnableTipou")),
                NoTCS = new(bin.IsBioPower, IsZero(bin, "E85Cal.ST_EthanolSensor")),
                DisableEmissionLimiting = new(emission is 0x02 or 0x03, emission == 0x03),
                DisableStartScreen = startScreen,
                DisableAdaptionMessages = adaption,
            };
        }

        // CheckValueIsZero: a missing symbol, or one that isn't a single byte, counts as zero
        private static bool IsZero(T7Binary bin, string name) =>
            bin.FindAny(name) is not { Length: 1 } sh || bin.ReadValue(sh)[0] == 0;

        // a single byte that is zero; what a missing symbol means differs per check
        private static bool IsZeroByte(T7Binary bin, string name, bool missing) =>
            bin.FindAny(name) is { Length: 1 } sh ? bin.ReadValue(sh)[0] == 0 : missing;

        private static bool SecondLambdaEnabled(T7Binary bin)
        {
            if (bin.FindAny("LambdaCal.ST_AdapEnable") is { Length: 1 } en && bin.ReadValue(en)[0] == 0) return false;
            if (bin.FindAny("O2HeatPostCal.I_LowLim") is { Length: 2 } lim && bin.ReadValue(lim) is [0, 0]) return false;
            return true;
        }

        private static bool ChecksumEnabledIn(T7Binary bin)
        {
            bool enabled = true;
            foreach (SymbolHelper sh in bin.Symbols)
            {
                if ((sh.Varname is "MapChkCal.ST_Enable" or "ROM339ChksmCal.ST_Enable" or "MapChk.ST_Enable") && sh.Length == 1 && bin.ReadValue(sh)[0] == 0)
                    enabled = false;
            }
            if (bin.Has("ROMChecksum.BottomOffFlash") || bin.Has("ROMChecksum.TopOffFlash") || bin.Has("RomChecksum.ActualChecksum"))
            {
                byte[] bottom = bin.Read((int)bin.SymbolAddress("ROMChecksum.BottomOffFlash"), bin.SymbolLength("ROMChecksum.BottomOffFlash"));
                byte[] top = bin.Read((int)bin.SymbolAddress("ROMChecksum.TopOffFlash"), bin.SymbolLength("ROMChecksum.TopOffFlash"));
                if (bottom.SequenceEqual(top)) enabled = false;
            }
            return enabled;
        }

        private static DateTime? ProgrammingDateTime(T7Binary bin)
        {
            byte[] d = bin.Read(0x7FD00, 6);
            if (d[0] == 0xFF) return null;
            try
            {
                return new DateTime(d[2] + 2000, d[1], d[0], d[3], d[4], d[5]);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        /// <summary>GetClosedIndicatorOffset: the first "Yes" (closed) or "No." / "No\0" (open) in the file.</summary>
        public static bool IsSidInfoOpen(T7Binary bin) => ClosedIndicator(File.ReadAllBytes(bin.FileName)).open;

        internal static (int offset, bool open) ClosedIndicator(byte[] data)
        {
            int state = 0;
            for (int i = 0; i < data.Length && i < FileT7.Length; i++)
            {
                byte b = data[i];
                switch (state)
                {
                    case 0: state = b == 'Y' ? 10 : b == 'N' ? 1 : 0; break;
                    case 1: state = b == 'o' ? 2 : 0; break;
                    case 2:
                        if (b == '.' || b == 0) return (i - 2, true);
                        state = 0;
                        break;
                    case 10: state = b == 'e' ? 11 : 0; break;
                    case 11:
                        if (b == 's') return (i - 2, false);
                        state = 0;
                        break;
                }
            }
            return (0, false);
        }

        // the start screen / adaption message patches only exist for a few known software versions
        private static (FirmwareOption startScreen, FirmwareOption adaption) SidOptions(T7Binary bin, string sw)
        {
            string v = sw.Trim();
            if (v is "EU0AF01C.55P" or "EU0AF01C.46T" or "ET03F01C.46S")
            {
                int w1 = Word(bin, 0x4968E), w2 = Word(bin, 0x496B4), w3 = Word(bin, 0x49760);
                bool ok = new[] { w1, w2, w3 }.All(w => w is 0x0000 or 0x0080);
                return (new(ok, w1 == 0 && w2 == 0), new(ok, w3 == 0));
            }
            if (v.StartsWith("ET02U01C"))
            {
                byte b1 = bin.Read(0x46F4D, 1)[0], b2 = bin.Read(0x4701F, 1)[0];
                bool ok = (b1 is 0x00 or 0x80) && (b2 is 0x00 or 0x80);
                return (new(ok, b1 == 0), new(ok, b2 == 0));
            }
            return (default, default);
        }

        private static int Word(T7Binary bin, int address)
        {
            byte[] b = bin.Read(address, 2);
            return b[0] << 8 | b[1];
        }
    }
}
