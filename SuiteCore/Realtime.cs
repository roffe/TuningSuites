using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CommonSuite
{
    /// <summary>A row of the realtime table (frmMain / Form1's RTSymbols DataTable).</summary>
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

        /// <summary>A row the panel made for a value that isn't read itself (KnockCyl1 from the per-cylinder counter, "Wideband").</summary>
        public bool Derived { get; set; }

        /// <summary>Read every Reload-th cycle (the suites' Delay / Reload columns).</summary>
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

    /// <summary>The symbols the dashboard's displays show where the suites named them differently.</summary>
    public sealed record DashboardSymbols(string Torque, string IgnitionOffset, string AirmassRequest, string Tps);

    /// <summary>What a map viewer's live cell follows: which axis value the engine's value is matched with.</summary>
    public enum CellInput { Rpm, Airmass, Tps, Torque, IgnitionOffset }

    /// <summary>Live cell tracking for maps whose name starts with one of Maps: the axes ("A|B" for B when the bin has no A) and the values to find on them.</summary>
    public sealed record CellRule(string[] Maps, string XAxis, CellInput X, double XScale, string YAxis, CellInput Y, double YScale);

    /// <summary>
    /// A suite's realtime table: its symbol names and conventions (FillRealtimeTable, the signed list, the per-cylinder counters,
    /// the status texts, UpdateOpenViewers' maps). The reading is the suite's RealtimeEngine.
    /// </summary>
    public abstract class RealtimeRules
    {
        /// <summary>Read as signed 16-bit when above 32000, whatever their length (the suites decided this by name).</summary>
        public abstract IReadOnlySet<string> Signed { get; }

        /// <summary>Per-cylinder counters that become rows of their own: symbol → (row name prefix, bytes per cylinder).</summary>
        public abstract IReadOnlyDictionary<string, (string Prefix, int Size)> PerCylinder { get; }

        /// <summary>FillRealtimeTable: the rows the dashboard shows, those the bin has.</summary>
        public abstract List<RealtimeSymbol> Dashboard(SuiteBinary bin, AppSettings settings);

        /// <summary>"Add to realtime list": the range presets by name, else by length.</summary>
        public abstract RealtimeSymbol FromSymbol(SymbolHelper sh);

        public abstract DashboardSymbols Symbols { get; }

        /// <summary>UpdateOpenViewers: the maps whose cell the engine is in.</summary>
        public abstract IReadOnlyList<CellRule> CellRules { get; }

        /// <summary>ConvertActiveAirDemand / ConvertLambdaStatus / ConvertFuelcutStatus.</summary>
        public abstract string AirDemand(int value);
        public abstract string Lambda(int value);
        public abstract string Fuelcut(int value);

        /// <summary>"t7l": the logs' extension.</summary>
        public abstract string LogExtension { get; }

        /// <summary>"Trionic 7 logfiles": the logs' open dialogs.</summary>
        public abstract string LogFilesName { get; }

        /// <summary>"t7rtl": the realtime layouts' extension.</summary>
        public abstract string LayoutExtension { get; }

        /// <summary>The wideband symbol Export logfile to LogWorks converts (T8Suite passed none).</summary>
        public virtual string LogWorksWidebandSymbol(AppSettings settings) => settings.WideBandSymbol;

        /// <summary>A row of the dashboard: the symbol's number, SRAM address and length from the bin; null when it has no such symbol.</summary>
        protected static RealtimeSymbol Row(SuiteBinary bin, string name, string description, double offset, double correction, double min, double max, int reload = 1)
        {
            if (bin.FindAny(name) is not { } sh) return null;
            return new RealtimeSymbol
            {
                Name = name, Description = description, SymbolNumber = sh.Symbol_number, SramAddress = sh.Start_address, Length = sh.Length,
                Offset = offset, Correction = correction, Minimum = min, Maximum = max, Reload = reload,
            };
        }

        /// <summary>A preset "Add to realtime list" row.</summary>
        protected static RealtimeSymbol UserRow(SymbolHelper sh, string name, double min, double max, double correction) => new()
        {
            Name = name, Description = sh.Description ?? "", SymbolNumber = sh.Symbol_number, SramAddress = sh.Start_address, Length = sh.Length,
            Minimum = min, Maximum = max, Correction = correction, UserDefined = true,
        };
    }

    /// <summary>The realtime table's conversions and layouts (Cycle, Save/LoadRealtimeTable), the same for every suite.</summary>
    public static class Realtime
    {
        /// <summary>Big-endian unsigned of 1, 2 or 4 bytes (anything else is 0), signed for the suite's signed names.</summary>
        public static double Decode(string name, byte[] d, IReadOnlySet<string> signed)
        {
            double v = d.Length switch
            {
                1 => d[0],
                2 => d[0] << 8 | d[1],
                4 => (uint)(d[0] << 24 | d[1] << 16 | d[2] << 8 | d[3]),
                _ => 0,
            };
            if (signed.Contains(name) && v > 32000) v = -(65536 - v);
            return v;
        }

        /// <summary>
        /// One pass: rows whose delay ran out (or every row with everyPass) are read and scaled, the others keep their last value.
        /// A read that fails (null) keeps the last value too; a row the panel derived isn't read. The per-cylinder counters add
        /// a row per cylinder after their own.
        /// </summary>
        public static RealtimeSample Cycle(IReadOnlyList<RealtimeSymbol> rows, Func<RealtimeSymbol, byte[]> read, DateTime now, RealtimeRules rules,
            double fps = 0, int? performanceMode = null, bool everyPass = false)
        {
            var values = new List<(string, double)>(rows.Count + 8);
            foreach (RealtimeSymbol row in rows)
            {
                if (row.Derived) continue;
                if (everyPass || --row.Delay <= 0)
                {
                    row.Delay = row.Reload;
                    if (read(row) is { } data)
                    {
                        row.Last = Decode(row.Name, data, rules.Signed) * row.Correction + row.Offset;
                        if (rules.PerCylinder.TryGetValue(row.Name, out var cyl))
                        {
                            for (int c = 0; c < 4 && (c + 1) * cyl.Size <= data.Length; c++)
                                values.Add(($"{cyl.Prefix}{c + 1}", cyl.Size == 1 ? data[c] : data[c * 2] << 8 | data[c * 2 + 1]));
                        }
                    }
                }
                values.Add((row.Name, row.Last));
            }
            return new RealtimeSample(now, values, fps, performanceMode);
        }

        /// <summary>HasExhaustGasTemperatureCalculation: ExhaustCal.ST_Enable is not 0 in the file.</summary>
        public static bool HasEgtCalculation(SuiteBinary bin) =>
            bin.FindAny("ExhaustCal.ST_Enable") is { } sh && bin.ReadSymbol(sh) is { Length: > 0 } d && d.Any(b => b != 0);

        /// <summary>
        /// rtsymbols.txt / layouts: the user rows as name|number|min|max|offset|correction|number|sram|length|description. Numbers
        /// are written invariant (the suites used the current culture); the description is the optional 10th field they read.
        /// </summary>
        public static void SaveLayout(string file, IEnumerable<RealtimeSymbol> rows)
        {
            string F(double d) => d.ToString(CultureInfo.InvariantCulture);
            File.WriteAllLines(file, rows.Where(r => r.UserDefined).Select(r =>
                $"{r.Name}|{r.SymbolNumber}|{F(r.Minimum)}|{F(r.Maximum)}|{F(r.Offset)}|{F(r.Correction)}|{r.SymbolNumber}|{r.SramAddress}|{r.Length}|{r.Description}"));
        }

        /// <summary>
        /// Reads a layout. The symbol's number, address and length come from the open bin when it has the symbol (the suites
        /// trusted the saved SRAM address); numbers with either decimal separator.
        /// </summary>
        public static List<RealtimeSymbol> LoadLayout(string file, SuiteBinary bin)
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
    /// The realtime loop: passes back to back on the ECU thread while connected, so map reads and other actions queue between
    /// two passes (the suites polled on the GUI thread and paused with m_prohibitReading). A suite's engine does the reading.
    /// </summary>
    public abstract class RealtimeEngine
    {
        private volatile RealtimeSymbol[] m_rows = [];

        /// <summary>The table, replaced as a whole; rows keep their last value and delay, so pass the same objects on.</summary>
        public IReadOnlyList<RealtimeSymbol> Rows
        {
            get => m_rows;
            set => m_rows = value.ToArray();
        }

        /// <summary>Values appended to every pass that don't come from the ECU (the serial wideband's "Wideband").</summary>
        public Func<IEnumerable<(string, double)>> Extra { get; set; }

        /// <summary>Raised on the ECU thread after every pass.</summary>
        public event Action<RealtimeSample> Sample;

        protected abstract bool Connected { get; }

        /// <summary>
        /// One pass over the rows, on the ECU thread; null when the session closed before it ran. A Disconnect queued behind the
        /// last pass runs before this one, while the loop still saw the session open.
        /// </summary>
        protected abstract Task<RealtimeSample> PassAsync(RealtimeSymbol[] rows, double fps);

        /// <summary>Before the first pass (T7: the alive polling stops, the passes keep the session alive).</summary>
        public virtual Task BeginAsync() => Task.CompletedTask;

        /// <summary>After the last pass, while still connected.</summary>
        public virtual Task EndAsync() => Task.CompletedTask;

        public async Task RunAsync(CancellationToken cancel)
        {
            var watch = Stopwatch.StartNew();
            double fps = 0;
            while (!cancel.IsCancellationRequested && Connected)
            {
                RealtimeSymbol[] rows = m_rows;
                if (rows.Length == 0)
                {
                    await Task.Delay(100, cancel).ConfigureAwait(false);
                    continue;
                }
                RealtimeSample sample = await PassAsync(rows, fps).ConfigureAwait(false);
                if (sample == null) break;
                if (Extra?.Invoke() is { } extra) sample = sample with { Values = [.. sample.Values, .. extra] };
                double seconds = watch.Elapsed.TotalSeconds;
                watch.Restart();
                fps = seconds > 0 ? 1 / seconds : 0;
                Sample?.Invoke(sample);
            }
        }
    }

    /// <summary>
    /// UpdateOpenViewers: the cell of a map the engine is in, the nearest axis breakpoint per axis (axes from the file, 16-bit
    /// values above 32000 negative). Axes are cached per tracker; make a new one for another bin.
    /// </summary>
    public sealed class CellTracker(SuiteBinary bin, IReadOnlyList<CellRule> rules)
    {
        private readonly Dictionary<string, int[]> m_axes = [];

        /// <summary>Column and data row of the map (by name prefix, as the suites matched viewers), or null when it isn't tracked.</summary>
        public (int col, int row)? Cell(string map, Func<CellInput, double> value)
        {
            CellRule rule = rules.FirstOrDefault(r => r.Maps.Any(map.StartsWith));
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

        // "A|B": B when the bin has no A
        private int[] Axis(string names)
        {
            if (m_axes.TryGetValue(names, out int[] a)) return a;
            a = [];
            string name = names.Split('|').FirstOrDefault(n => bin.FindAny(n) != null) ?? names;
            if (bin.FindAny(name) is { } sh && bin.ReadSymbol(sh) is { } d)
            {
                if (bin.IsSixteenBitTable(name))
                    a = Enumerable.Range(0, d.Length / 2).Select(i => d[i * 2] << 8 | d[i * 2 + 1]).Select(v => v > 32000 ? v - 65536 : v).ToArray();
                else a = d.Select(b => (int)b).ToArray();
            }
            return m_axes[names] = a;
        }
    }

    /// <summary>The wideband conversions the panel and the AFR maps share.</summary>
    public static class WidebandAfr
    {
        public const double Stoich = 14.7;

        /// <summary>
        /// ConvertToWidebandAFR: a 0..1023 ADC count to AFR with the configured voltages (settings ×1000). The count is scaled
        /// over HighV − LowV without adding LowV, as the suites did.
        /// </summary>
        public static double AdcToAfr(double adc, AppSettings s)
        {
            double lowV = s.WidebandLowVoltage / 1000.0, highV = s.WidebandHighVoltage / 1000.0;
            double lowAfr = s.WidebandLowAFR / 1000.0, highAfr = s.WidebandHighAFR / 1000.0;
            if (highV <= lowV) return lowAfr;
            double v = Math.Clamp(adc / 1023 * (highV - lowV), lowV, highV);
            return lowAfr + (highAfr - lowAfr) / (highV - lowV) * (v - lowV);
        }

        /// <summary>The wideband AFR from the ECU symbol's realtime value (AD_Scanner is an ADC count, LambdaScanner AFR already ×0.1).</summary>
        public static double? SymbolAfr(RealtimeSample sample, AppSettings s) =>
            sample[s.WideBandSymbol] is { } v ? s.WideBandSymbol == "DisplProt.AD_Scanner" ? AdcToAfr(v, s) : v : null;
    }
}
