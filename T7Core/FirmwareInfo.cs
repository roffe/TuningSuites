using System;
using System.IO;
using System.Linq;
using CommonSuite;
using TrionicCANLib.Checksum;
using TrionicCANLib.Firmware;

namespace T7
{
    /// <summary>What the firmware information dialog lets the user change.</summary>
    public class FirmwareEdit
    {
        public string CarDescription { get; set; } = "";
        public string SoftwareVersion { get; set; } = "";
        public string ImmobilizerID { get; set; } = "";
        public string ChassisID { get; set; } = "";
        public string SIDDate { get; set; } = "";
        public DateTime? ProgrammingDate { get; set; }
        public bool OpenSIDInfo { get; set; }
        public bool TorqueLimiters { get; set; }
        public bool CatalystLightOff { get; set; }
        public bool SecondLambda { get; set; }
        public bool OBDII { get; set; }
        public bool FastThrottleResponse { get; set; }
        public bool ExtraFastThrottleResponse { get; set; }
        public bool NoTCS { get; set; }
        public bool DisableEmissionLimiting { get; set; }
        public bool DisableStartScreen { get; set; }
        public bool DisableAdaptionMessages { get; set; }

        public static FirmwareEdit From(FirmwareInfo info) => new()
        {
            CarDescription = info.CarDescription,
            SoftwareVersion = info.SoftwareVersion,
            ImmobilizerID = info.ImmobilizerID,
            ChassisID = info.ChassisID,
            SIDDate = info.SIDDate,
            ProgrammingDate = info.ProgrammingDate,
            OpenSIDInfo = info.OpenSIDInfo.Enabled,
            TorqueLimiters = info.TorqueLimiters.Enabled,
            CatalystLightOff = info.CatalystLightOff.Enabled,
            SecondLambda = info.SecondLambda.Enabled,
            OBDII = info.OBDII.Enabled,
            FastThrottleResponse = info.FastThrottleResponse.Enabled,
            ExtraFastThrottleResponse = info.ExtraFastThrottleResponse.Enabled,
            NoTCS = info.NoTCS.Enabled,
            DisableEmissionLimiting = info.DisableEmissionLimiting.Enabled,
            DisableStartScreen = info.DisableStartScreen.Enabled,
            DisableAdaptionMessages = info.DisableAdaptionMessages.Enabled,
        };
    }

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

        /// <summary>
        /// The OK path of frmMain's firmware information handler, in its order: TIS footer fix (asked), header fields,
        /// programming stamp, open/closed SID, the option patches (each re-checked against the file as it is then), footer
        /// save, SID start screen / adaption patches, the EU0AF01C emission byte, checksum. Option writes get transaction
        /// entries when a project log is given; the SID / emission patches never did.
        /// </summary>
        public static void Apply(T7Binary bin, FirmwareEdit edit, bool autoFixFooter, bool writeTimestamp,
            Func<string, bool> askYesNo, TrionicTransactionLog log = null)
        {
            string file = bin.FileName;
            var header = new T7FileHeader();
            header.init(file, autoFixFooter);
            string sw = header.getSoftwareVersion();

            if (header.IsTISBinary(file) && (edit.ImmobilizerID != header.getImmobilizerID() || edit.ChassisID != header.getChassisID())
                && askYesNo("It seems you are trying to update data in a TIS file, would you like T7Suite to correct the footer information?"))
            {
                File.Copy(file, Path.Combine(Path.GetDirectoryName(file), Path.GetFileNameWithoutExtension(file) + DateTime.Now.ToString("yyyyMMddHHmmss") + ".binarybackup"), true);
                header.init(file, true);
            }
            // footer strings are rewritten in place: keep each at its original length
            header.setImmobilizerID(Fit(edit.ImmobilizerID, header.getImmobilizerID()));
            header.setSoftwareVersion(Fit(edit.SoftwareVersion, header.getSoftwareVersion()));
            header.setCarDescription(Fit(edit.CarDescription, header.getCarDescription()));
            header.setChassisID(Fit(edit.ChassisID, header.getChassisID()));
            header.setSIDDate(Fit(edit.SIDDate, header.getSIDDate()));

            if (writeTimestamp && edit.ProgrammingDate is DateTime d && ProgrammingDateTime(bin) != d)
                bin.WriteData(0x7FD00, [(byte)d.Day, (byte)d.Month, (byte)(d.Year - 2000), (byte)d.Hour, (byte)d.Minute, (byte)d.Second], log);

            var (offset, open) = ClosedIndicator(File.ReadAllBytes(file));
            if (offset > 0 && edit.OpenSIDInfo && !open) bin.WriteData(offset, "No."u8.ToArray(), log);
            if (offset > 0 && !edit.OpenSIDInfo && open) bin.WriteData(offset, "Yes"u8.ToArray(), log);

            void Set(string name, byte value)
            {
                if (bin.FindAny(name) is { } sh) bin.WriteData((int)bin.AddressOf(sh), [value], log);
            }
            // SetX: write the byte only when it changes (the !=0 / ==0 checks of the old helpers)
            void SetIfDifferent(string name, bool on, byte onValue)
            {
                if (bin.FindAny(name) is not { Length: 1 } sh) return;
                byte current = bin.ReadValue(sh)[0];
                if (on && current == 0) bin.WriteData((int)bin.AddressOf(sh), [onValue], log);
                else if (!on && current != 0) bin.WriteData((int)bin.AddressOf(sh), [0], log);
            }

            bool hasLimiter = bin.Has("TorqueCal.ST_Loop");
            bool limiterOn = !IsZeroByte(bin, "TorqueCal.ST_Loop", missing: false);
            if (hasLimiter && edit.TorqueLimiters != limiterOn) SetIfDifferent("TorqueCal.ST_Loop", edit.TorqueLimiters, 0x02);

            bool obd2 = bin.Has("OBDCal.OBD2Enabled");
            bool obdOn = obd2 ? !IsZero(bin, "OBDCal.OBD2Enabled") : !IsZero(bin, "OBDCal.EOBDEnabled") || !IsZero(bin, "OBDCal.LOBDEnabled");
            if (edit.OBDII != obdOn)
            {
                if (obd2)
                {
                    SetIfDifferent("OBDCal.OBD2Enabled", edit.OBDII, 0x01);
                }
                else if (edit.OBDII)
                {
                    bool european = askYesNo("Do you want to set the European OBD2 tests active? If you choose No, the generic 'Rest of the world' setting will be set");
                    SetIfDifferent(european ? "OBDCal.EOBDEnabled" : "OBDCal.LOBDEnabled", true, 0x01);
                }
                else
                {
                    SetIfDifferent("OBDCal.EOBDEnabled", false, 0);
                    SetIfDifferent("OBDCal.LOBDEnabled", false, 0);
                }
            }

            // checked: written on every OK while the map exists; unchecked: only when it is on
            if (bin.Has("LambdaCal.ST_AdapEnable") && (edit.SecondLambda || SecondLambdaEnabled(bin)))
            {
                SetIfDifferent("LambdaCal.ST_AdapEnable", edit.SecondLambda, 0x01);
                if (bin.FindAny("O2HeatPostCal.I_LowLim") is { Length: 2 } lim)
                    bin.WriteData((int)bin.AddressOf(lim), edit.SecondLambda ? [0x00, 0xE6] : [0x00, 0x00], log);
            }

            bool tipInOut = bin.Symbols.Cast<SymbolHelper>().Any(sh => sh.Varname.StartsWith("EngTipCal.") || sh.Userdescription.StartsWith("EngTipCal."));
            if (tipInOut)
            {
                bool Fast() => IsZero(bin, "EngTipCal.ST_EnableTipin") && IsZero(bin, "EngTipCal.ST_EnableTipou");
                bool Extra() => Fast() && IsZero(bin, "EngTipCal.ST_EnableActG2");
                // SetFastThrottleResponse: Tipin/Tipou 0 (fast) or 1, ActG2 always 1; SetExtraFast: all three 0 or 1
                if (edit.FastThrottleResponse != Fast())
                {
                    byte v = edit.FastThrottleResponse ? (byte)0 : (byte)1;
                    Set("EngTipCal.ST_EnableTipin", v);
                    Set("EngTipCal.ST_EnableActG2", 1);
                    Set("EngTipCal.ST_EnableTipou", v);
                }
                if (edit.ExtraFastThrottleResponse && !Extra())
                {
                    Set("EngTipCal.ST_EnableTipin", 0);
                    Set("EngTipCal.ST_EnableActG2", 0);
                    Set("EngTipCal.ST_EnableTipou", 0);
                }
                else if (!edit.ExtraFastThrottleResponse && !edit.FastThrottleResponse && Extra())
                {
                    Set("EngTipCal.ST_EnableTipin", 1);
                    Set("EngTipCal.ST_EnableActG2", 1);
                    Set("EngTipCal.ST_EnableTipou", 1);
                }
                else if (!edit.ExtraFastThrottleResponse && edit.FastThrottleResponse && Extra())
                {
                    Set("EngTipCal.ST_EnableActG2", 1);
                }
            }

            bool lightOff = bin.Symbols.Cast<SymbolHelper>().Any(sh => sh.Varname.StartsWith("IgnLOffCal.") || sh.Userdescription.StartsWith("IgnLOffCal."));
            if (lightOff)
            {
                bool on = !(IsZero(bin, "IdleCal.ST_EnableLOffRpm") && IsZero(bin, "IgnLOffCal.ST_Enable"));
                if (edit.CatalystLightOff != on)
                {
                    Set("IdleCal.ST_EnableLOffRpm", edit.CatalystLightOff ? (byte)1 : (byte)0);
                    Set("IgnLOffCal.ST_Enable", edit.CatalystLightOff ? (byte)1 : (byte)0);
                }
            }

            // BioPower itself can't be switched (it would put the car in limp home); the ethanol sensor can ("No TCS" inverted)
            if (bin.IsBioPower)
            {
                bool sensor = !edit.NoTCS;
                if (sensor == IsZero(bin, "E85Cal.ST_EthanolSensor")) Set("E85Cal.ST_EthanolSensor", sensor ? (byte)1 : (byte)0);
            }

            header.save(file);

            var (startScreen, adaption) = SidOptions(bin, sw);
            if (startScreen.Available)
            {
                if (sw.Trim().StartsWith("ET02U01C"))
                {
                    bin.WriteData(0x46F4D, [edit.DisableStartScreen ? (byte)0x00 : (byte)0x80]);
                    bin.WriteData(0x4701F, [edit.DisableAdaptionMessages ? (byte)0x00 : (byte)0x80]);
                }
                else
                {
                    byte[] screen = edit.DisableStartScreen ? [0x00, 0x00] : [0x00, 0x80];
                    bin.WriteData(0x4968E, screen);
                    bin.WriteData(0x496B4, screen);
                    bin.WriteData(0x49760, edit.DisableAdaptionMessages ? [0x00, 0x00] : [0x00, 0x80]);
                }
            }
            // as in T7Suite this is written for every EU0AF01C bin, whatever the byte was
            if (sw.Trim().StartsWith("EU0AF01C")) bin.WriteData(0x13837, [edit.DisableEmissionLimiting ? (byte)0x03 : (byte)0x02]);

            bin.UpdateChecksum(autoFixFooter);
        }

        private static string Fit(string value, string original) =>
            (value ?? "").Length >= original.Length ? (value ?? "")[..original.Length] : value.PadRight(original.Length);

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
