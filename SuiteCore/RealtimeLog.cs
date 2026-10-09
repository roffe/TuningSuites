using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CommonSuite
{
    public sealed record RealtimeLogLine(DateTime Time, List<(string Name, double Value)> Values)
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

    /// <summary>
    /// The realtime logs (.t7l / .t8l): "dd/MM/yyyy HH:mm:ss.fff|name=value|...|IMPORTANTLINE=0|", one file per bin and day next
    /// to the bin. Written with invariant numbers and separators (the suites used the current culture, so a log from an English
    /// Windows read back as zeros on a Swedish one); read with either decimal separator. Also what the log viewer and the CSV
    /// export read (RealtimeGraphControl.ImportT5Logfile, CSVGenerator).
    /// </summary>
    public static class RealtimeLog
    {
        public static string FileName(string binFile, DateTime day, string extension) =>
            Path.Combine(Path.GetDirectoryName(binFile) ?? "", $"{Path.GetFileNameWithoutExtension(binFile)}-{day:yyyyMMdd}-CanTraceExt.{extension}");

        public static string Line(DateTime time, IEnumerable<(string Name, double Value)> values, bool marker)
        {
            var sb = new StringBuilder(time.ToString("dd/MM/yyyy HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append('|');
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

        /// <summary>The lines that parse, in file order.</summary>
        public static List<RealtimeLogLine> Read(string file)
        {
            var lines = new List<RealtimeLogLine>();
            foreach (string line in File.ReadLines(file))
                if (TryParse(line, out DateTime t, out var values)) lines.Add(new RealtimeLogLine(t, values));
            return lines;
        }

        /// <summary>
        /// AnalyseFile: a gap of 10 seconds or more starts a new section (sessions appended to the day's file). The first line
        /// after a gap belongs to the new section (the suites dropped it).
        /// </summary>
        public static List<List<RealtimeLogLine>> Sections(List<RealtimeLogLine> lines, double gapSeconds = 10)
        {
            var sections = new List<List<RealtimeLogLine>>();
            foreach (RealtimeLogLine line in lines)
            {
                if (sections.Count == 0 || (line.Time - sections[^1][^1].Time).TotalSeconds >= gapSeconds) sections.Add([]);
                sections[^1].Add(line);
            }
            return sections;
        }

        /// <summary>"HH:mm:ss - HH:mm:ss [duration]", the section dialog's text.</summary>
        public static string Describe(List<RealtimeLogLine> section) =>
            $"{section[0].Time:HH:mm:ss} - {section[^1].Time:HH:mm:ss} [{section[^1].Time - section[0].Time:hh\\:mm\\:ss}]";

        /// <summary>
        /// Log filters: a line is dropped when an active filter on a symbol in the line fails (GreaterThan keeps values above or
        /// at the filter, SmallerThan below or at, Equals only equal ones).
        /// </summary>
        public static bool Passes(RealtimeLogLine line, IEnumerable<LogFilter> filters)
        {
            foreach (LogFilter f in filters)
            {
                if (!f.Active || line[f.Symbol] is not { } v) continue;
                bool ok = f.Type switch
                {
                    LogFilter.MathType.GreaterThan => v >= f.Value,
                    LogFilter.MathType.SmallerThan => v <= f.Value,
                    _ => v == f.Value,
                };
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>The names in first-seen order (the selection dialog's symbols).</summary>
        public static List<string> Symbols(IEnumerable<RealtimeLogLine> lines)
        {
            var names = new List<string>();
            var seen = new HashSet<string>();
            foreach (RealtimeLogLine line in lines)
                foreach (var (n, _) in line.Values)
                    if (seen.Add(n)) names.Add(n);
            return names;
        }

        /// <summary>GetGraphName: the viewer's channel names for the well known (T5 / T7) symbols; others keep their names.</summary>
        public static string DisplayName(string symbol) => symbol.ToLowerInvariant() switch
        {
            "in.v_vehicle" => "Speed",
            "actualin.n_engine" => "Rpm",
            "in.p_airinlet" => "Boost",
            "actualin.t_engine" => "Coolant",
            "actualin.t_airinlet" => "IAT",
            "ecmstat.st_activeairdem" => "LIMITER",
            "ignprot.fi_offset" => "IOFF",
            "m_request" => "Request",
            "out.m_engine" => "Torque",
            "ecmstat.p_engine" => "Power",
            "out.pwm_boostcntrl" => "APC PWM",
            "out.fi_ignition" => "Ign.angle",
            "out.x_accpedal" => "TPS",
            "maf.m_airinlet" => "Airmass",
            "exhaust.t_calc" => "EGT",
            "displprot.lambdascanner" => "WBLambda",
            "lambda.lambdaint" => "NBLambda",
            _ => symbol,
        };

        /// <summary>
        /// Export logfile to CSV: "Time,&lt;columns&gt;" then seconds since the first line (F4) and the values, invariant. A
        /// column missing from a line stays empty (the suites skipped it and shifted the rest left).
        /// </summary>
        public static void ExportCsv(IReadOnlyList<RealtimeLogLine> lines, IReadOnlyList<string> columns, string file)
        {
            var sb = new StringBuilder("Time");
            foreach (string c in columns) sb.Append(',').Append(c);
            sb.Append('\n');
            if (lines.Count > 0)
            {
                DateTime start = lines[0].Time;
                foreach (RealtimeLogLine line in lines)
                {
                    sb.Append((line.Time - start).TotalSeconds.ToString("F4", CultureInfo.InvariantCulture));
                    foreach (string c in columns) sb.Append(',').Append(line[c]?.ToString(CultureInfo.InvariantCulture));
                    sb.Append('\n');
                }
            }
            File.WriteAllText(file, sb.ToString());
        }
    }

    /// <summary>Appends log lines to the day's file of the bin; a new file after midnight.</summary>
    public sealed class RealtimeLogWriter(string binFile, string extension) : IDisposable
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
                CurrentFile = RealtimeLog.FileName(binFile, m_day, extension);
                m_writer = new StreamWriter(CurrentFile, true) { AutoFlush = true };
            }
            m_writer.WriteLine(RealtimeLog.Line(sample.Time, sample.Values, marker));
        }

        public void Dispose()
        {
            m_writer?.Dispose();
            m_writer = null;
        }
    }
}
