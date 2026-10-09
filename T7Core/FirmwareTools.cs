using System;
using System.Collections.Generic;
using CommonSuite;

namespace T7
{
    /// <summary>The TCM limiter modification's state (frmTcmLimit).</summary>
    public sealed record TcmLimit(bool ThresholdMod, int TorqueLimit, bool GearMod, int Gear, int GearLimit);

    /// <summary>Actions → ESP calibration and TCM Limit (frmMain 4360-4443) over T7EspEdit / TCMLimitEdit.</summary>
    public static class FirmwareTools
    {
        /// <summary>frmEspSelection's choices: the byte after F0 03 34 4E 75.</summary>
        public static readonly (byte value, string name)[] EspCalibrations =
        [
            (0x82, "15 inch brakes TCS/ESP B205"),
            (0x90, "15/16 inch brakes TCS/ESP B235E MY07 -> and B235R MY00-01"),
            (0x91, "16 inch brakes ESP B235L"),
            (0x92, "16+ inch brakes ESP B235R MY02 ->"),
        ];

        /// <summary>The ESP calibration byte, or null when the file doesn't have it ("File not compatible!").</summary>
        public static byte? ReadEsp(T7Binary bin)
        {
            var esp = new T7EspEdit();
            return esp.loadFile(bin.FileName) ? esp.getEspValue() : null;
        }

        public static void WriteEsp(T7Binary bin, byte value, bool autoFixFooter)
        {
            var esp = new T7EspEdit();
            if (!esp.loadFile(bin.FileName)) throw new InvalidOperationException("File not compatible!");
            esp.setEspValue(value);
            esp.saveFile();
            bin.UpdateChecksum(autoFixFooter);
        }

        /// <summary>MOD v1's choices (TorqueLimit, Nm into VIOSCal.M_TCMOffset).</summary>
        public static readonly (int value, string name)[] TcmThresholds =
        [
            (0, "0 Nm : Full torque on all gears"), (285, "285 Nm : 4spd 9-3 transmission"), (295, "295 Nm : 4spd 9-3 Aero transmission"),
            (305, "305 Nm : 4spd 9-5 transmission"), (325, "325 Nm : 4spd 9-5 Aero transmission"), (345, "345 Nm : 5spd 9-5 transmission"),
        ];

        /// <summary>MOD v2's gears and limits.</summary>
        public static readonly (int value, string name)[] TcmGears =
            [(2, "Reverse"), (3, "Neutral"), (5, "Gear 1"), (6, "Gear 2"), (7, "Gear 3"), (8, "Gear 4"), (9, "Gear 5")];

        public static readonly (int value, string name)[] TcmGearLimits =
        [
            (0x10E, "279 Nm : highest gears 480 Nm"), (0x11F, "287 Nm : highest gears 497 Nm"), (0x12C, "300 Nm : highest gears 450 Nm"),
            (0x13F, "319 Nm : highest gears 499 Nm"), (0x14E, "334 Nm : highest gears 484 Nm"), (0x15E, "350 Nm : highest gears 485 Nm"),
            (0x16D, "365 Nm : highest gears 470 Nm"), (0x17E, "382 Nm : highest gears 487 Nm"), (0x18E, "398 Nm : highest gears 488 Nm"),
        ];

        private const string TcmOffset = "VIOSCal.M_TCMOffset";

        /// <summary>The current state, or null when the bin lacks VIOSCal.M_TCMOffset or the firmware code.</summary>
        public static TcmLimit ReadTcm(T7Binary bin)
        {
            if (bin.FindAny(TcmOffset) is not { } sh || bin.ReadSymbol(sh) is not { } d) return null;
            var tcm = new TCMLimitEdit();
            if (!tcm.loadFile(bin.FileName, bin.FileAddress(sh))) return null;
            return new TcmLimit(tcm.getModificationEnabled(), d.Length == 2 ? d[0] << 8 | d[1] : 0, tcm.getGearLimitModificationEnabled(), tcm.Gear, tcm.Limit);
        }

        /// <summary>
        /// Ok: the firmware code for the chosen modification, then the limit into VIOSCal.M_TCMOffset (0 when both are off) with a
        /// transaction entry "TCM Limit modification VIOSCal.M_TCMOffset", then the checksum. Nothing when nothing changed.
        /// </summary>
        public static void WriteTcm(T7Binary bin, TcmLimit before, TcmLimit after, bool autoFixFooter, TrionicTransactionLog log = null)
        {
            if (before == after) return;
            SymbolHelper sh = bin.FindAny(TcmOffset) ?? throw new InvalidOperationException("File not compatible, symbol VIOSCal.M_TCMOffset missing!");
            var tcm = new TCMLimitEdit();
            if (!tcm.loadFile(bin.FileName, bin.FileAddress(sh))) throw new InvalidOperationException("File not compatible!");
            tcm.setGearLimitModificationEnabled(after.GearMod);
            tcm.setModificationEnabled(after.ThresholdMod);
            tcm.Gear = after.Gear;
            tcm.Limit = after.GearLimit;
            tcm.saveFile();
            int torque = after.ThresholdMod || after.GearMod ? after.TorqueLimit : 0;
            if (torque != before.TorqueLimit)
                bin.WriteData(bin.FileAddress(sh), [(byte)(torque >> 8), (byte)torque], log, "TCM Limit modification VIOSCal.M_TCMOffset");
            bin.UpdateChecksum(autoFixFooter);
        }
    }
}
