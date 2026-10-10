using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using CommonSuite;

namespace Trionic5Tools
{
    /// <summary>
    /// The "Tuning wizards" page's file work (frmMain's handlers over Trionic5Tuner), for the T5 windows. Each returns the report
    /// lines the old suite showed in its TuningReport. The tuner writes with raw file I/O (no transaction entries, as T5Suite);
    /// the checksum is updated afterwards.
    /// </summary>
    public static class T5Tuning
    {
        private static List<string> Lines(Trionic5Tuner t) => t.Resume.ResumeTuning.Rows.Cast<DataRow>().Select(r => r[0]?.ToString() ?? "").ToList();

        private static Trionic5Tuner Tuner(bool autoChecksum) => new() { AutoUpdateChecksum = autoChecksum };

        // ---- Tune me up ----

        /// <summary>The wizard's defaults: the current stage (stock → 1), markers, knock time, RPM limit and the boost valve type.</summary>
        public sealed record TuneDefaults(int Stage, MapSensorType MapSensor, InjectorType Injectors, TurboType Turbo, int KnockTime, int RpmLimit, BPCType Valve);

        /// <summary>RunTuningWizard's checks and presets; null when the file was already tuned beyond stage 3.</summary>
        public static TuneDefaults TuneMeUpDefaults(T5Binary bin)
        {
            Trionic5Properties p = bin.File.GetTrionicProperties();
            if ((int)p.TuningStage > 3) return null;
            int frek230 = bin.File.GetSymbolAsInt("Frek_230!"), frek250 = bin.File.GetSymbolAsInt("Frek_250!");
            bool t5Valve = bin.IsTrionic55 ? frek230 == 90 || frek250 == 70 : frek230 == 728 || frek250 == 935;
            return new TuneDefaults(Math.Max((int)p.TuningStage, 1), p.MapSensorType, p.InjectorType, p.TurboType, bin.File.GetSymbolAsInt("Knock_matrix_time!"),
                bin.File.GetSymbolAsInt("Rpm_max!") * 10, t5Valve ? BPCType.Trionic5Valve : BPCType.Trionic7Valve);
        }

        /// <summary>Stage 1-3: TuneFileToStage without asking (the wizard asked); with a backup next to the file.</summary>
        public static (TuningResult result, List<string> report) TuneToStage(T5Binary bin, int stage, bool autoChecksum)
        {
            Trionic5Tuner t = Tuner(autoChecksum);
            TuningResult r = t.TuneFileToStage(stage, bin.FileName, bin.File, bin.Info, true);
            return (r, Lines(t));
        }

        /// <summary>Stage 4 and higher: FreeTuneBinary with the wizard's settings.</summary>
        public static (TuningResult result, List<string> report) FreeTune(T5Binary bin, double peakTorque, double peakBoost, bool byTorque, MapSensorType sensor,
            TurboType turbo, InjectorType injectors, BPCType valve, int rpmLimit, int knockTime, bool autoChecksum)
        {
            Trionic5Tuner t = Tuner(autoChecksum);
            TuningResult r = t.FreeTuneBinary(bin.File, peakTorque, peakBoost, byTorque, sensor, turbo, injectors, valve, rpmLimit, knockTime);
            // the writes after TuneToStage got no checksum of their own (T5Suite left it stale)
            if (r == TuningResult.TuningSuccess) bin.UpdateChecksum();
            return (r, Lines(t));
        }

        // ---- MAP sensor ----

        public static string SensorName(MapSensorType t) => t switch
        {
            MapSensorType.MapSensor30 => "3.0 bar mapsensor",
            MapSensorType.MapSensor35 => "3.5 bar mapsensor",
            MapSensorType.MapSensor40 => "4.0 bar mapsensor",
            MapSensorType.MapSensor50 => "5.0 bar mapsensor",
            _ => "2.5 bar mapsensor",
        };

        /// <summary>
        /// Convert to a different MAP sensor: every boost related table and axis rescaled from the current sensor to the chosen one
        /// (T5Suite converted to 3.0 bar whatever entry was chosen), the marker set, the checksum updated.
        /// </summary>
        public static List<string> ConvertMapSensor(T5Binary bin, MapSensorType from, MapSensorType to, bool autoChecksum)
        {
            Trionic5Tuner t = Tuner(autoChecksum);
            t.ConvertFileToThreeBarMapSensor(bin.Info, from, to);
            bin.UpdateChecksum();
            return Lines(t);
        }

        // ---- injectors ----

        /// <summary>The injector wizard's start: the injector type marker, Inj_konst!, the battery correction map (ms, 15 V first) and the crank factor.</summary>
        public sealed record InjectorState(InjectorType Type, int Constant, double[] BatteryCorrection, double CrankFactor);

        public static InjectorState Injectors(T5Binary bin)
        {
            Trionic5Properties p = bin.File.GetTrionicProperties();
            int constant = bin.File.GetSymbolAsInt("Inj_konst!");
            double[] batt = (bin.File.GetSymbolAsIntArray("Batt_korr_tab!") ?? []).Select(v => v * 0.004).ToArray();
            return new InjectorState(p.InjectorType, constant, batt, bin.File.GetSymbolAsInt("Start_insp!") * 0.004);
        }

        /// <summary>frmInjectorWizard's proposal for another injector type: the constant moved by the known difference, its battery correction and crank factor.</summary>
        public static InjectorState Propose(InjectorState current, InjectorType to)
        {
            int diff = new Trionic5Tuner().DetermineDifferenceInInjectorConstant(current.Type, to);
            double crank = to switch
            {
                InjectorType.Siemens630Dekas => 6,
                InjectorType.Siemens875Dekas => 4,
                InjectorType.Siemens1000cc => 3.5,
                _ => 9,
            };
            double[] batt = to switch
            {
                InjectorType.GreenGiants => [0.894, 1.003, 1.15, 1.308, 1.521, 1.768, 2.102, 2.545, 3.216, 4.142, 5.45],
                InjectorType.Siemens630Dekas => [0.33, 0.433, 0.548, 0.673, 0.802, 0.974, 1.208, 1.524, 2.023, 2.74, 3.6],
                // stock; T5Suite proposed the stock values for the 875 and 1000 cc injectors too
                _ => [0.59, 0.77, 0.78, 0.94, 1.28, 1.50, 1.85, 2.32, 3.73, 3.73, 3.73],
            };
            return new InjectorState(to, current.Constant - diff, batt, crank);
        }

        /// <summary>
        /// The injector wizard's finish: Batt_korr_tab! (16-bit, ms / 0.004), Inj_konst! and Start_insp! (crank / 0.004), then the
        /// injector marker and the checksum. T5Suite wrote them without transaction entries; here they are logged when a project is open.
        /// </summary>
        public static void ApplyInjectors(T5Binary bin, InjectorState s, CommonSuite.TrionicTransactionLog log)
        {
            void Write(string name, byte[] data)
            {
                if (bin.Find(name) is { } sh && bin.FileAddress(sh) is var a and >= 0) bin.WriteData(a, data, log, "Injector wizard");
            }
            Write("Batt_korr_tab!", s.BatteryCorrection.SelectMany(ms => Word((int)Math.Round(ms / 0.004))).ToArray());
            Write("Inj_konst!", [(byte)Math.Clamp(s.Constant, 0, 255)]);
            Write("Start_insp!", Word((int)Math.Round(s.CrankFactor / 0.004)));
            Trionic5Properties p = bin.File.GetTrionicProperties();
            p.InjectorType = s.Type;
            bin.File.SetTrionicOptions(p);
            bin.UpdateChecksum();
        }

        private static byte[] Word(int v) => [(byte)(v >> 8), (byte)v];

        // ---- E85 ----

        /// <summary>Convert to E85: a backup (the project's Backups folder, else next to the file), ConvertToE85, the checksum.</summary>
        public static List<string> ConvertToE85(T5Binary bin, string backupFolder, bool autoChecksum)
        {
            string folder = backupFolder ?? Path.GetDirectoryName(bin.FileName) ?? "";
            Directory.CreateDirectory(folder);
            File.Copy(bin.FileName, Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(bin.FileName)}-backup-{DateTime.Now:MMddyyyyHHmmss}.BIN"), true);
            Trionic5Tuner t = Tuner(autoChecksum);
            t.ConvertToE85(bin.File);
            bin.UpdateChecksum();
            return Lines(t);
        }

        // ---- code patches ----

        public sealed record BoostAdaption(int ManualLow, int ManualHigh, int AutomaticLow, int AutomaticHigh, int BoostError);

        public static BoostAdaption ReadBoostAdaption(T5Binary bin) =>
            new(bin.File.GetManualRpmLow(), bin.File.GetManualRpmHigh(), bin.File.GetAutoRpmLow(), bin.File.GetAutoRpmHigh(), bin.File.GetMaxBoostError());

        /// <summary>Change boost adaption ranges: the code patched; true when all three patterns were found (then the footer keeps the values).</summary>
        public static bool SetBoostAdaption(T5Binary bin, BoostAdaption to, bool autoChecksum)
        {
            BoostAdaption ori = ReadBoostAdaption(bin);
            Trionic5Tuner t = Tuner(autoChecksum);
            t.Ori_boostError = ori.BoostError;
            t.Ori_rpmHighAut = ori.AutomaticHigh;
            t.Ori_rpmHighManual = ori.ManualHigh;
            t.Ori_rpmLowAut = ori.AutomaticLow;
            t.Ori_rpmLowManual = ori.ManualLow;
            bool ok = t.SetBoostAdaptionParameters(to.ManualLow, to.ManualHigh, to.AutomaticLow, to.AutomaticHigh, to.BoostError, bin.Info);
            if (ok)
            {
                bin.File.SetAutoRpmHigh(to.AutomaticHigh);
                bin.File.SetAutoRpmLow(to.AutomaticLow);
                bin.File.SetManualRpmHigh(to.ManualHigh);
                bin.File.SetManualRpmLow(to.ManualLow);
                bin.File.SetMaxBoostError(to.BoostError);
            }
            bin.UpdateChecksum();
            return ok;
        }

        /// <summary>Change boost bias range: the axis step patched in the code; true when the code was found.</summary>
        public static bool SetBoostBiasStep(T5Binary bin, int step, bool autoChecksum)
        {
            int original = bin.File.GetRegulationDivisorValue();
            bool ok = Tuner(autoChecksum).SetBoostRegulationDivisor(step, original, bin.Info);
            if (ok) bin.File.SetRegulationDivisorValue(step);
            bin.UpdateChecksum();
            return ok;
        }

        /// <summary>The hardcoded RPM limit (T5.5 code pattern) and Rpm_max! × 10; null when the wizard can't change them.</summary>
        public static (int hardcoded, int software)? ReadRpmLimits(T5Binary bin)
        {
            if (!bin.IsTrionic55) return null;
            int hard = bin.File.GetHardcodedRPMLimit(bin.FileName);
            return hard > 0 && bin.File.GetHardcodedRPMLimitTwo(bin.FileName) > 0 ? (hard, bin.File.GetSymbolAsInt("Rpm_max!") * 10) : null;
        }

        /// <summary>Change RPM limit: both code sequences and Rpm_max! (rpm / 10), then the checksum.</summary>
        public static void SetRpmLimits(T5Binary bin, int hardcoded, int software, CommonSuite.TrionicTransactionLog log)
        {
            bin.File.SetHardcodedRPMLimit(bin.FileName, hardcoded);
            if (bin.Find("Rpm_max!") is { } sh && bin.FileAddress(sh) is var a and >= 0) bin.WriteData(a, Word(software / 10), log, "RPM limiter wizard");
            bin.UpdateChecksum();
        }
    }
}
