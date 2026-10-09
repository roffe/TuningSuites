using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommonSuite;

namespace T7
{
    /// <summary>
    /// The wideband's AFR (frmMain.ConvertToWidebandAFR and the serial reader), the AFR target / feedback / counter maps on
    /// BFuelCal.Map's axes (LogWidebandAFR, ShowAfrMAP, Import AFR feedback data). Files live in &lt;bin dir&gt;/AFRMaps.
    /// </summary>
    public sealed class AfrFeedback
    {
        public const int Columns = 18, Rows = 16;
        public const double Stoich = WidebandAfr.Stoich;

        public AFRMap Map { get; } = new();
        public T7Binary Binary { get; }
        public int Size { get; }

        private readonly int[] m_rpm, m_air;

        public AfrFeedback(T7Binary bin)
        {
            Binary = bin;
            Size = bin.FindAny("BFuelCal.Map")?.Length ?? Columns * Rows;
            m_rpm = Axis(bin, "BFuelCal.RpmYSP");
            m_air = Axis(bin, "BFuelCal.AirXSP");
            Map.RpmYSP = m_rpm;
            Map.AirXSP = m_air;
            Map.InitializeMaps(Size, bin.FileName);
        }

        // GetSymbolAsIntArray: 16-bit axes from the file
        private static int[] Axis(T7Binary bin, string name) =>
            bin.FindAny(name) is { } sh && bin.ReadSymbol(sh) is { } d ? Enumerable.Range(0, d.Length / 2).Select(i => d[i * 2] << 8 | d[i * 2 + 1]).ToArray() : [];

        /// <summary>
        /// LogWidebandAFR: a sample into its cell's running mean (AFR, or λ when measuring in lambda), when rpm &gt; 600,
        /// 0 ≤ afr &lt; 25 and there is no fuel cut. False when the sample was dropped.
        /// </summary>
        public bool Add(double afr, bool inLambda, double rpm, double airmass, double fuelcut)
        {
            if (rpm <= 600 || afr < 0 || afr >= 25 || fuelcut != 0 || m_rpm.Length == 0 || m_air.Length == 0) return false;
            int r = Nearest(m_rpm, rpm), a = Nearest(m_air, airmass);
            Map.AddMeasurement((float)(inLambda ? afr / Stoich : afr), r, a, Columns, Rows);
            return true;
        }

        private static int Nearest(int[] axis, double value)
        {
            int best = 0;
            for (int i = 1; i < axis.Length; i++)
                if (Math.Abs(axis[i] - value) < Math.Abs(axis[best] - value)) best = i;
            return best;
        }

        public string TargetFile => Path.Combine(Path.GetDirectoryName(Binary.FileName) ?? "", "AFRMaps", Path.GetFileNameWithoutExtension(Binary.FileName) + "-targetafr.afr");

        /// <summary>The maps as 16-bit values ×10, for a viewer with factor 0.1 (the counter as plain counts).</summary>
        public byte[] Target => Map.GetTargetAFRMapinBytes(Size, Binary.FileName);
        public byte[] Feedback => Map.GetFeedbackMapInBytes(Size);
        public byte[] Counter => Map.GetFeedbackCounterMapInBytes(Size);

        public void Save() => Map.SaveMap(Binary.FileName, Columns, Rows);

        public void Clear() => Map.ClearMaps(Columns, Rows, Binary.FileName);

        /// <summary>
        /// onTargetAFRMapSave: the edited target map (16-bit ×10) into its file. The samples are saved first, so reloading the
        /// maps keeps them (T7Suite lost the unsaved ones).
        /// </summary>
        public void SaveTarget(byte[] data)
        {
            var target = new float[data.Length / 2];
            for (int i = 0; i < target.Length; i++) target[i] = (data[i * 2] << 8 | data[i * 2 + 1]) / 10f;
            Save();
            Map.SaveTargetAFRMap(TargetFile, target, Columns, Rows);
            Map.InitializeMaps(Size, Binary.FileName);
        }

        /// <summary>
        /// Import AFR feedback data: every measured cell of the fuel map moved by the full error, lean up, rich down, 1..254.
        /// In lambda mode the feedback is λ and is compared as AFR (T7Suite compared λ to the AFR target).
        /// </summary>
        public static byte[] ApplyFeedback(byte[] fuel, byte[] target, byte[] feedback, byte[] counter, bool inLambda)
        {
            var result = (byte[])fuel.Clone();
            for (int i = 0; i < fuel.Length && i * 2 + 1 < target.Length; i++)
            {
                int count = counter[i * 2] << 8 | counter[i * 2 + 1];
                double tgt = (target[i * 2] << 8 | target[i * 2 + 1]) / 10.0, fb = (feedback[i * 2] << 8 | feedback[i * 2 + 1]) / 10.0;
                if (count == 0 || tgt == 0) continue;
                if (inLambda) fb *= Stoich;
                double d = Math.Abs(tgt - fb) / tgt * 100;
                double v = fb > tgt ? fuel[i] * (100 + d) / 100 : fuel[i] * (100 - d) / 100;
                result[i] = (byte)Math.Clamp(Math.Round(v), 1, 254);
            }
            return result;
        }
    }

    /// <summary>
    /// Autotune (fuel only, open binaries): optionally closed loop / E85 / fuel cut switched off in SRAM, the fuel map read from
    /// SRAM, and every sample through AFRMap.HandleRealtimeData; with auto update each corrected cell is written to SRAM at once.
    /// </summary>
    public sealed class Autotune
    {
        private readonly AfrFeedback m_afr;
        private readonly T7Ecu m_ecu;
        private readonly AppSettings m_settings;
        private readonly Dictionary<SymbolHelper, byte[]> m_switched = [];

        public SymbolHelper FuelMap { get; }
        public bool AutoUpdate => m_settings.AutoUpdateFuelMap;

        /// <summary>A cell reached its stable time and was judged (T7Suite played ping.wav).</summary>
        public event Action CellLocked;

        private Autotune(AfrFeedback afr, T7Ecu ecu, AppSettings settings, SymbolHelper fuelMap)
        {
            m_afr = afr;
            m_ecu = ecu;
            m_settings = settings;
            FuelMap = fuelMap;
            afr.Map.onFuelmapCellChanged += (_, e) =>
            {
                if (AutoUpdate) _ = m_ecu.RunAsync(t => t.WriteMapToSRAM((uint)(FuelMap.Start_address + e.Mapindex), [e.Cellvalue]));
            };
            afr.Map.onCellLocked += (_, _) => CellLocked?.Invoke();
        }

        /// <summary>Why autotune can't start, or null.</summary>
        public static string CannotStart(T7Binary bin, AppSettings s, double coolant)
        {
            if (!s.UseWidebandLambda && !s.UseDigitalWidebandLambda) return "Autotune needs a wideband lambda sensor, see the settings";
            if (coolant < 70) return "Engine temperature of 70 degrees C not reached...";
            if (!bin.IsSoftwareOpen) return "Autotune is only available for OPEN binaries";
            return null;
        }

        /// <summary>Start: the ST_Enable switches off (when configured), the fuel map from SRAM, the settings into AFRMap.</summary>
        public static async Task<Autotune> StartAsync(AfrFeedback afr, T7Ecu ecu, AppSettings s)
        {
            string name = string.IsNullOrEmpty(s.AutoTuneFuelMap) ? "BFuelCal.Map" : s.AutoTuneFuelMap;
            if (afr.Binary.FindAny(name) is not { } fuel) return null;
            var tune = new Autotune(afr, ecu, s, fuel);
            if (s.DisableClosedLoopOnStartAutotune)
            {
                var switches = new List<string> { "LambdaCal.ST_Enable", "FCutCal.ST_Enable" };
                if (afr.Binary.IsBioPower) switches.Insert(1, "E85Cal.ST_Enable");
                foreach (string sw in switches)
                {
                    if (afr.Binary.FindAny(sw) is not { Length: > 0 } sh) continue;
                    byte[] before = await ecu.ReadMapAsync(sh);
                    var off = (byte[])before.Clone();
                    off[0] = 0;
                    if (await ecu.WriteMapAsync(sh, off)) tune.m_switched[sh] = before;
                }
            }
            byte[] map = await ecu.ReadMapAsync(fuel);
            afr.Map.InitAutoTuneVars(false, AfrFeedback.Columns, AfrFeedback.Rows);
            afr.Map.SetCurrentFuelMap(map);
            afr.Map.SetOriginalFuelMap(map);
            afr.Map.AutoUpdateFuelMap = s.AutoUpdateFuelMap;
            afr.Map.CorrectionPercentage = s.CorrectionPercentage;
            afr.Map.AcceptableTargetErrorPercentage = s.AcceptableTargetErrorPercentage;
            afr.Map.CellStableTime_ms = s.CellStableTime_ms;
            afr.Map.MaximumAdjustmentPerCyclePercentage = s.MaximumAdjustmentPerCyclePercentage;
            return tune;
        }

        /// <summary>ProcessAutoTuning: one sample, always in AFR.</summary>
        public void Handle(double afr, double rpm, double airmass) => m_afr.Map.HandleRealtimeData((float)afr, (float)rpm, (float)airmass);

        /// <summary>Stop: the switches back as they were.</summary>
        public async Task RestoreAsync()
        {
            foreach (var (sh, before) in m_switched) await m_ecu.WriteMapAsync(sh, before);
            m_switched.Clear();
        }

        public byte[] Original => m_afr.Map.GetOriginalFuelmap();

        /// <summary>Without auto update: the proposed change per cell in percent (data order, rpm × 18 + airmass).</summary>
        public double[] Differences => m_afr.Map.GetPercentualDifferences();

        /// <summary>acceptMap_onUpdateFuelMap: the original values moved by the accepted percentages.</summary>
        public static byte[] Accept(byte[] original, double[] percent, IEnumerable<int> cells)
        {
            var result = (byte[])original.Clone();
            foreach (int i in cells)
                if (i < result.Length && i < percent.Length && percent[i] != 0 && !double.IsNaN(percent[i]))
                    result[i] = (byte)Math.Clamp(Math.Round(original[i] * (100 + percent[i]) / 100), 0, 255);
            return result;
        }
    }
}
