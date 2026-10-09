using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommonSuite;

namespace T7
{
    /// <summary>A row of the realtime table (frmMain's RTSymbols DataTable).</summary>
    public sealed class RealtimeSymbol
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public int SymbolNumber { get; set; } = -1;
        public long SramAddress { get; set; }
        public int Length { get; set; }
        public double Offset { get; set; }
        public double Correction { get; set; } = 1;
        public double Minimum { get; set; }
        public double Maximum { get; set; } = 255;
        public bool UserDefined { get; set; }

        /// <summary>Read every Reload-th cycle (T7Suite's Delay / Reload columns).</summary>
        public int Reload { get; set; } = 1;

        // ECU thread only
        internal int Delay;
        internal double Last;

        public RealtimeSymbol Clone() => (RealtimeSymbol)MemberwiseClone();
    }

    /// <summary>One pass over the table: every row's value (the last one for rows not read this time), in table order.</summary>
    public sealed record RealtimeSample(DateTime Time, IReadOnlyList<(string Name, double Value)> Values, double Fps, int? PerformanceMode)
    {
        public double? this[string name]
        {
            get
            {
                foreach (var (n, v) in Values)
                    if (n == name) return v;
                return null;
            }
        }
    }

    /// <summary>The realtime table and its conversions (frmMain.FillRealtimeTable, GetSRAMVarsFromTable, Save/LoadRealtimeTable).</summary>
    public static class Realtime
    {
        /// <summary>Read as signed 16-bit when above 32000, whatever their length (T7Suite decided this by name).</summary>
        public static readonly HashSet<string> Signed =
        [
            "ActualIn.T_Engine", "ActualIn.T_AirInlet", "Out.fi_Ignition", "Out.M_Engine", "ECMStat.P_Engine", "ECMStat.p_Diff",
            "IgnProt.fi_Offset", "IgnKnk.fi_MeanKnock", "Ign.fi_OtherOff", "IgnJerkProt.fi_Offset", "Lambda.LambdaInt",
            "MAF.m_AirInlet", "AdpFuelProt.MulFuelAdapt", "BoostProt.PFac", "BoostProt.IFac", "BoostProt.LoadDiff",
        ];

        /// <summary>Big-endian unsigned of 1, 2 or 4 bytes (anything else is 0), signed for the names above.</summary>
        public static double Decode(string name, byte[] d)
        {
            double v = d.Length switch
            {
                1 => d[0],
                2 => d[0] << 8 | d[1],
                4 => (uint)(d[0] << 24 | d[1] << 16 | d[2] << 8 | d[3]),
                _ => 0,
            };
            if (Signed.Contains(name) && v > 32000) v = -(65536 - v);
            return v;
        }

        // the 8 / 12 byte per-cylinder counters become rows of their own
        private static string DerivedPrefix(string name) => name switch
        {
            "KnkDet.KnockCyl" or "KnkDetAdap.KnkCntCyl" => "KnockCyl",
            "MissfAdap.MissfCntCyl" => "MisfCyl",
            _ => null,
        };

        /// <summary>
        /// One pass: rows whose delay ran out are read and scaled, the others keep their last value. A failed read keeps the
        /// last value too (T7Suite's failed short read threw and lost the rest of the pass).
        /// </summary>
        public static RealtimeSample Cycle(IReadOnlyList<RealtimeSymbol> rows, Func<RealtimeSymbol, byte[]> read, DateTime now, double fps = 0, int? performanceMode = null)
        {
            var values = new List<(string, double)>(rows.Count + 8);
            foreach (RealtimeSymbol row in rows)
            {
                if (--row.Delay <= 0)
                {
                    row.Delay = row.Reload;
                    if (Readable(row) && read(row) is { } data)
                    {
                        row.Last = Decode(row.Name, data) * row.Correction + row.Offset;
                        if (DerivedPrefix(row.Name) is { } prefix)
                        {
                            for (int c = 0; c < 4 && c * 2 + 1 < data.Length; c++)
                                values.Add(($"{prefix}{c + 1}", data[c * 2] << 8 | data[c * 2 + 1]));
                        }
                    }
                }
                values.Add((row.Name, row.Last));
            }
            return new RealtimeSample(now, values, fps, performanceMode);
        }

        private static bool Readable(RealtimeSymbol row) => row.Length is > 0 and <= 4 ? row.SramAddress > 0 : row.Length > 4 && row.SymbolNumber >= 0;

        /// <summary>ReadSymbolFromSRAM: up to 4 bytes by SRAM address (data from byte 1 of the reply), longer by symbol number.</summary>
        public static byte[] Read(TrionicCANLib.API.Trionic7 t, RealtimeSymbol row)
        {
            bool ok;
            if (row.Length <= 4)
            {
                byte[] reply = t.ReadValueFromSRAM(row.SramAddress, row.Length, out ok);
                return ok && reply.Length > row.Length ? reply[1..(row.Length + 1)] : null;
            }
            byte[] data = t.ReadSymbolNumber((uint)row.SymbolNumber, out ok);
            return ok ? data : null;
        }

        private static RealtimeSymbol Row(T7Binary bin, string name, string description, double offset, double correction, double min, double max, int reload = 1)
        {
            if (bin.FindAny(name) is not { } sh) return null;
            return new RealtimeSymbol
            {
                Name = name, Description = description, SymbolNumber = sh.Symbol_number, SramAddress = sh.Start_address, Length = sh.Length,
                Offset = offset, Correction = correction, Minimum = min, Maximum = max, Reload = reload,
            };
        }

        /// <summary>FillRealtimeTable(Dashboard): the rows the dashboard shows, those the bin has.</summary>
        public static List<RealtimeSymbol> Dashboard(T7Binary bin, AppSettings settings)
        {
            var rows = new List<RealtimeSymbol>
            {
                Row(bin, "ActualIn.n_Engine", "Engine speed", 0, 1, 0, 8000),
                Row(bin, "In.v_Vehicle", "Vehicle speed", 0, 0.1, 0, 300, 3),
                Row(bin, "Out.X_AccPedal", "TPS %", 0, 0.1, 0, 100),
                Row(bin, "ActualIn.T_Engine", "Engine temperature", 0, 1, -20, 120, 5),
                Row(bin, "ActualIn.T_AirInlet", "Intake air temperature", 0, 1, -20, 120, 3),
                Row(bin, "ECMStat.ST_ActiveAirDem", "Active air demand map", 0, 1, 0, 255),
                Row(bin, "Lambda.Status", "Lambda status", 0, 1, 0, 255),
                Row(bin, "FCut.CutStatus", "Fuelcut status", 0, 1, 0, 255),
                Row(bin, "IgnProt.fi_Offset", "Ignition offset", 0, 0.1, -20, 20),
                Row(bin, "m_Request", "Requested airmass", 0, 1, 0, 600),
                Row(bin, "Out.M_Engine", "Calculated torque", 0, 1, 0, 600),
                Row(bin, "In.p_AirInlet", "Boost", -1, 0.001, -1, 3),
                Row(bin, "Out.PWM_BoostCntrl", "Duty cycle BCV", 0, 0.1, 0, 100),
                Row(bin, "Out.fi_Ignition", "Ignition advance", 0, 0.1, -10, 50),
                Row(bin, "MAF.m_AirInlet", "Actual airmass", 0, 1, 0, 1600),
            };
            if (HasEgtCalculation(bin)) rows.Add(Row(bin, "Exhaust.T_Calc", "Calculated EGT temperature", 0, 1, 0, 1200, 2));
            rows.Add(Row(bin, "BFuelProt.CurrentFuelCon", "Fuel consumption", 0, 0.1, 0, 50, 2));
            if (!settings.UseWidebandLambda) rows.Add(Row(bin, "Lambda.LambdaInt", "Lambda value (nbO2)", 1, 0.0001, 0, 2));
            else
            {
                double correction = settings.WideBandSymbol == "DisplProt.LambdaScanner" ? 0.1 : 1;
                rows.Add(Row(bin, settings.WideBandSymbol, "Lambda value (wbO2)", 0, correction, 10, 20));
            }
            return rows.Where(r => r != null).ToList();
        }

        /// <summary>HasExhaustGasTemperatureCalculation: ExhaustCal.ST_Enable is not 0 in the file.</summary>
        public static bool HasEgtCalculation(T7Binary bin) =>
            bin.FindAny("ExhaustCal.ST_Enable") is { } sh && bin.ReadSymbol(sh) is { Length: > 0 } d && d.Any(b => b != 0);

        /// <summary>"Add to realtime list" from the symbol list: the range presets by name, else by length.</summary>
        public static RealtimeSymbol FromSymbol(SymbolHelper sh)
        {
            string name = sh.Varname.StartsWith("Symbol") && sh.Userdescription != "" ? sh.Userdescription : sh.Varname;
            var (min, max, correction) = name switch
            {
                "Torque.M_MaxEngAndGear" => (0d, 255d, 1d),
                "BFuelProt.t_InjActual" => (0, 255, 0.001),
                "ActualIn.v_Vehicle2" or "In.v_Vehicle" => (0, 255, 0.1),
                "ActualIn.U_LambdaCat" or "ActualIn.U_LambdaEng" => (0, 1200, 1),
                "TorqueProt.m_AirTMasLim" or "AirctlData.Actual" => (0, 1800, 1),
                _ => (0, sh.Length == 1 ? 255 : 65535, 1),
            };
            return new RealtimeSymbol
            {
                Name = name, Description = sh.Description ?? "", SymbolNumber = sh.Symbol_number, SramAddress = sh.Start_address, Length = sh.Length,
                Minimum = min, Maximum = max, Correction = correction, UserDefined = true,
            };
        }

        /// <summary>
        /// rtsymbols.txt / .t7rtl: the user rows as name|number|min|max|offset|correction|number|sram|length|description. Numbers
        /// are written invariant (T7Suite used the current culture); the description is the optional 10th field T7Suite reads.
        /// </summary>
        public static void SaveLayout(string file, IEnumerable<RealtimeSymbol> rows)
        {
            string F(double d) => d.ToString(CultureInfo.InvariantCulture);
            File.WriteAllLines(file, rows.Where(r => r.UserDefined).Select(r =>
                $"{r.Name}|{r.SymbolNumber}|{F(r.Minimum)}|{F(r.Maximum)}|{F(r.Offset)}|{F(r.Correction)}|{r.SymbolNumber}|{r.SramAddress}|{r.Length}|{r.Description}"));
        }

        /// <summary>
        /// Reads a layout. The symbol's number, address and length come from the open bin when it has the symbol (T7Suite trusted
        /// the saved SRAM address); numbers with either decimal separator.
        /// </summary>
        public static List<RealtimeSymbol> LoadLayout(string file, T7Binary bin)
        {
            var rows = new List<RealtimeSymbol>();
            if (!File.Exists(file)) return rows;
            foreach (string line in File.ReadAllLines(file))
            {
                string[] f = line.Split('|');
                if (f.Length < 9) continue;
                var row = new RealtimeSymbol
                {
                    Name = f[0], Description = f.Length > 9 ? f[9] : f[0], Minimum = LogFile.Number(f[2]), Maximum = LogFile.Number(f[3]),
                    Offset = LogFile.Number(f[4]), Correction = LogFile.Number(f[5]), UserDefined = true,
                };
                if (bin?.FindAny(f[0]) is { } sh)
                {
                    row.SymbolNumber = sh.Symbol_number;
                    row.SramAddress = sh.Start_address;
                    row.Length = sh.Length;
                }
                else
                {
                    row.SymbolNumber = int.TryParse(f[1], out int n) ? n : -1;
                    row.SramAddress = long.TryParse(f[7], out long a) ? a : 0;
                    row.Length = int.TryParse(f[8], out int l) ? l : 0;
                }
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>The dashboard rows, then the user rows the dashboard doesn't have.</summary>
        public static List<RealtimeSymbol> Merge(IEnumerable<RealtimeSymbol> dashboard, IEnumerable<RealtimeSymbol> user)
        {
            var rows = dashboard.ToList();
            var names = rows.Select(r => r.Name).ToHashSet();
            rows.AddRange(user.Where(u => names.Add(u.Name)));
            return rows;
        }

        /// <summary>Calculated power [hp] from rpm and torque [Nm], as the dashboard shows it.</summary>
        public static double Power(double rpm, double torque) => rpm * torque / 7121;
    }

    /// <summary>
    /// The realtime loop: passes back to back on the ECU thread while connected, so map reads, DTC actions and the like queue
    /// between two passes (T7Suite polled on the GUI thread and paused with m_prohibitReading). Performance.Mode every 21 passes.
    /// </summary>
    public sealed class RealtimeEngine(T7Ecu ecu)
    {
        private volatile RealtimeSymbol[] m_rows = [];

        /// <summary>The table, replaced as a whole; rows keep their last value and delay, so pass the same objects on.</summary>
        public IReadOnlyList<RealtimeSymbol> Rows
        {
            get => m_rows;
            set => m_rows = value.ToArray();
        }

        /// <summary>Performance.Mode, read every 21 passes when the bin has it.</summary>
        public SymbolHelper PerformanceMode { get; set; }

        /// <summary>Values appended to every pass that don't come from the ECU (the serial wideband's "Wideband").</summary>
        public Func<IEnumerable<(string, double)>> Extra { get; set; }

        /// <summary>Raised on the ECU thread after every pass.</summary>
        public event Action<RealtimeSample> Sample;

        public async Task RunAsync(CancellationToken cancel)
        {
            int pass = 0;
            var watch = Stopwatch.StartNew();
            double fps = 0;
            while (!cancel.IsCancellationRequested && ecu.IsConnected)
            {
                RealtimeSymbol[] rows = m_rows;
                if (rows.Length == 0)
                {
                    await Task.Delay(100, cancel).ConfigureAwait(false);
                    continue;
                }
                bool mode = ++pass % 21 == 0 && PerformanceMode is { } pm;
                RealtimeSample sample = await ecu.RunAsync(t =>
                {
                    int? performance = mode ? ReadPerformanceMode(t, PerformanceMode) : null;
                    return Realtime.Cycle(rows, row => Realtime.Read(t, row), DateTime.Now, fps, performance);
                }).ConfigureAwait(false);
                if (Extra?.Invoke() is { } extra) sample = sample with { Values = [.. sample.Values, .. extra] };
                double seconds = watch.Elapsed.TotalSeconds;
                watch.Restart();
                fps = seconds > 0 ? 1 / seconds : 0;
                Sample?.Invoke(sample);
            }
        }

        // the last byte: 0 / 'E' eco, 1 / 'N' normal, 2 / 'S' sport
        private static int? ReadPerformanceMode(TrionicCANLib.API.Trionic7 t, SymbolHelper sh)
        {
            byte[] data = Realtime.Read(t, new RealtimeSymbol { Name = sh.SmartVarname, SymbolNumber = sh.Symbol_number, SramAddress = sh.Start_address, Length = sh.Length });
            return data is { Length: > 0 } ? data[^1] switch { 0 or (byte)'E' => 0, 1 or (byte)'N' => 1, 2 or (byte)'S' => 2, _ => null } : null;
        }

        /// <summary>SetPerformanceMode: the mode in the last byte of Performance.Mode, written by SRAM address.</summary>
        public Task<bool> SetPerformanceModeAsync(int mode) => ecu.RunAsync(t =>
        {
            if (PerformanceMode is not { Length: > 0 } sh) return false;
            var data = new byte[sh.Length];
            data[^1] = (byte)mode;
            return t.WriteMapToSRAM((uint)sh.Start_address, data);
        });
    }

    /// <summary>
    /// UpdateOpenViewers: the cell of a map the engine is in, the nearest axis breakpoint per axis (axes from the file, 16-bit
    /// values above 32000 negative). Axes are cached per tracker; make a new one for another bin.
    /// </summary>
    public sealed class CellTracker(T7Binary bin)
    {
        public enum Input { Rpm, Airmass, Tps, Torque, IgnitionOffset }

        private sealed record Rule(string[] Maps, string XAxis, Input X, double XScale, string YAxis, Input Y, double YScale);

        private static readonly Rule[] s_rules =
        [
            new(["BFuelCal.Map", "BFuelCal.StartMap", "BFuelCal.E85Map", "BFuelCal.GasMap", "MyrtilosCal.Fuel_GasMap", "MyrtilosAdap.WBLambda_FeedbackMap", "MyrtilosAdap.WBLambda_FFMap"],
                "BFuelCal.AirXSP", Input.Airmass, 1, "BFuelCal.RpmYSP", Input.Rpm, 1),
            new(["KnkFuelCal.EnrichmentMap"], "IgnKnkCal.m_AirXSP", Input.Airmass, 1, "IgnKnkCal.n_EngYSP", Input.Rpm, 1),
            new(["InjAnglCal.Map"], "InjAnglCal.AirXSP", Input.Airmass, 1, "InjAnglCal.RpmYSP", Input.Rpm, 1),
            new(["IgnNormCal.Map", "IgnNormCal.GasMap", "IgnE85Cal.fi_AbsMap"], "IgnNormCal.m_AirXSP", Input.Airmass, 1, "IgnNormCal.n_EngYSP", Input.Rpm, 1),
            new(["KnkFuelCal.fi_MapMaxOff"], "KnkFuelCal.m_AirXSP", Input.Airmass, 1, "BstKnkCal.n_EngYSP", Input.Rpm, 1),
            new(["IgnKnkCal.IndexMap"], "IgnKnkCal.m_AirXSP", Input.Airmass, 1, "IgnKnkCal.n_EngYSP", Input.Rpm, 1),
            new(["KnkDetCal.RefFactorMap"], "KnkDetCal.m_AirXSP", Input.Airmass, 1, "KnkDetCal.n_EngYSP", Input.Rpm, 1),
            new(["PedalMapCal.m_RequestMap"], "PedalMapCal.n_EngineMap", Input.Rpm, 1, "PedalMapCal.X_PedalMap", Input.Tps, 0.1),
            new(["TorqueCal.m_AirTorqMap"], "TorqueCal.M_EngXSP", Input.Torque, 1, "TorqueCal.n_EngYSP", Input.Rpm, 1),
            new(["TorqueCal.M_NominalMap"], "TorqueCal.m_AirXSP", Input.Airmass, 1, "TorqueCal.n_EngYSP", Input.Rpm, 1),
            new(["BoostCal.RegMap"], "BoostCal.SetLoadXSP", Input.Airmass, 1, "BoostCal.n_EngSP", Input.Rpm, 1),
            new(["BstKnkCal.MaxAirmass"], "BstKnkCal.OffsetXSP", Input.IgnitionOffset, 0.1, "BstKnkCal.n_EngYSP", Input.Rpm, 1),
        ];

        private readonly Dictionary<string, int[]> m_axes = [];

        /// <summary>Column and data row of the map (by name prefix, as T7Suite matched viewers), or null when it isn't tracked.</summary>
        public (int col, int row)? Cell(string map, Func<Input, double> value)
        {
            Rule rule = s_rules.FirstOrDefault(r => r.Maps.Any(map.StartsWith));
            if (rule == null) return null;
            int x = Nearest(rule.XAxis, value(rule.X), rule.XScale), y = Nearest(rule.YAxis, value(rule.Y), rule.YScale);
            return x < 0 || y < 0 ? null : (x, y);
        }

        private int Nearest(string axis, double value, double scale)
        {
            int[] a = Axis(axis);
            int best = -1;
            double min = double.MaxValue;
            for (int i = 0; i < a.Length; i++)
            {
                double diff = Math.Abs(a[i] * scale - value);
                if (diff < min)
                {
                    min = diff;
                    best = i;
                }
            }
            return best;
        }

        private int[] Axis(string name)
        {
            if (m_axes.TryGetValue(name, out int[] a)) return a;
            a = [];
            if (bin.FindAny(name) is { } sh && bin.ReadSymbol(sh) is { } d)
            {
                if (bin.IsSixteenBitTable(name))
                    a = Enumerable.Range(0, d.Length / 2).Select(i => d[i * 2] << 8 | d[i * 2 + 1]).Select(v => v > 32000 ? v - 65536 : v).ToArray();
                else a = d.Select(b => (int)b).ToArray();
            }
            return m_axes[name] = a;
        }
    }

    /// <summary>
    /// .t7l logs: "dd/MM/yyyy HH:mm:ss.fff|name=value|...|IMPORTANTLINE=0|", one file per bin and day next to the bin. Written
    /// with invariant numbers and separators (T7Suite used the current culture, so a log from an English Windows read back as
    /// zeros on a Swedish one); read with either decimal separator.
    /// </summary>
    public static class T7Log
    {
        public static string FileName(string binFile, DateTime day) =>
            Path.Combine(Path.GetDirectoryName(binFile) ?? "", $"{Path.GetFileNameWithoutExtension(binFile)}-{day:yyyyMMdd}-CanTraceExt.t7l");

        public static string Line(DateTime time, IEnumerable<(string Name, double Value)> values, bool marker)
        {
            var sb = new System.Text.StringBuilder(time.ToString("dd/MM/yyyy HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append('|');
            foreach (var (n, v) in values) sb.Append(n).Append('=').Append(v.ToString("G15", CultureInfo.InvariantCulture)).Append('|');
            return sb.Append("IMPORTANTLINE=").Append(marker ? '1' : '0').Append('|').ToString();
        }

        /// <summary>A line's time (by position, any one-character separators) and its name=value fields, or false.</summary>
        public static bool TryParse(string line, out DateTime time, out List<(string Name, double Value)> values)
        {
            values = [];
            time = default;
            string[] f = line.Split('|');
            string t = f[0];
            if (t.Length < 19) return false;
            try
            {
                int ms = t.Length >= 23 && int.TryParse(t.AsSpan(20, 3), out int m) ? m : 0;
                time = new DateTime(int.Parse(t.AsSpan(6, 4)), int.Parse(t.AsSpan(3, 2)), int.Parse(t.AsSpan(0, 2)),
                    int.Parse(t.AsSpan(11, 2)), int.Parse(t.AsSpan(14, 2)), int.Parse(t.AsSpan(17, 2)), ms);
            }
            catch (Exception e) when (e is FormatException or ArgumentOutOfRangeException)
            {
                return false;
            }
            for (int i = 1; i < f.Length; i++)
            {
                string[] kv = f[i].Split('=');
                if (kv.Length == 2) values.Add((kv[0], LogFile.Number(kv[1])));
            }
            return true;
        }
    }

    /// <summary>Appends .t7l lines, the day's file of the bin; a new file after midnight.</summary>
    public sealed class T7LogWriter(string binFile) : IDisposable
    {
        private StreamWriter m_writer;
        private DateTime m_day;

        public string CurrentFile { get; private set; }

        public void Write(RealtimeSample sample, bool marker)
        {
            if (m_writer == null || sample.Time.Date != m_day)
            {
                m_writer?.Dispose();
                m_day = sample.Time.Date;
                CurrentFile = T7Log.FileName(binFile, m_day);
                m_writer = new StreamWriter(CurrentFile, true) { AutoFlush = true };
            }
            m_writer.WriteLine(T7Log.Line(sample.Time, sample.Values, marker));
        }

        public void Dispose()
        {
            m_writer?.Dispose();
            m_writer = null;
        }
    }
}
