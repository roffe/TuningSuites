using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CommonSuite;

namespace T7
{
    public sealed record T7LogLine(DateTime Time, List<(string Name, double Value)> Values)
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

    /// <summary>Reading .t7l logs for the log viewer and the CSV export (RealtimeGraphControl.ImportT5Logfile, CSVGenerator).</summary>
    public static class T7LogFile
    {
        /// <summary>The lines that parse, in file order.</summary>
        public static List<T7LogLine> Read(string file)
        {
            var lines = new List<T7LogLine>();
            foreach (string line in File.ReadLines(file))
                if (T7Log.TryParse(line, out DateTime t, out var values)) lines.Add(new T7LogLine(t, values));
            return lines;
        }

        /// <summary>
        /// AnalyseFile: a gap of 10 seconds or more starts a new section (sessions appended to the day's file). The first line
        /// after a gap belongs to the new section (T7Suite dropped it).
        /// </summary>
        public static List<List<T7LogLine>> Sections(List<T7LogLine> lines, double gapSeconds = 10)
        {
            var sections = new List<List<T7LogLine>>();
            foreach (T7LogLine line in lines)
            {
                if (sections.Count == 0 || (line.Time - sections[^1][^1].Time).TotalSeconds >= gapSeconds) sections.Add([]);
                sections[^1].Add(line);
            }
            return sections;
        }

        /// <summary>"HH:mm:ss - HH:mm:ss [duration]", the section dialog's text.</summary>
        public static string Describe(List<T7LogLine> section) =>
            $"{section[0].Time:HH:mm:ss} - {section[^1].Time:HH:mm:ss} [{section[^1].Time - section[0].Time:hh\\:mm\\:ss}]";

        /// <summary>
        /// Log filters: a line is dropped when an active filter on a symbol in the line fails (GreaterThan keeps values above or
        /// at the filter, SmallerThan below or at, Equals only equal ones).
        /// </summary>
        public static bool Passes(T7LogLine line, IEnumerable<LogFilter> filters)
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
        public static List<string> Symbols(IEnumerable<T7LogLine> lines)
        {
            var names = new List<string>();
            var seen = new HashSet<string>();
            foreach (T7LogLine line in lines)
                foreach (var (n, _) in line.Values)
                    if (seen.Add(n)) names.Add(n);
            return names;
        }

        /// <summary>GetGraphName: the viewer's channel names for the well known symbols.</summary>
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
        /// column missing from a line stays empty (T7Suite skipped it and shifted the rest left).
        /// </summary>
        public static void ExportCsv(IReadOnlyList<T7LogLine> lines, IReadOnlyList<string> columns, string file)
        {
            var sb = new StringBuilder("Time");
            foreach (string c in columns) sb.Append(',').Append(c);
            sb.Append('\n');
            if (lines.Count > 0)
            {
                DateTime start = lines[0].Time;
                foreach (T7LogLine line in lines)
                {
                    sb.Append((line.Time - start).TotalSeconds.ToString("F4", CultureInfo.InvariantCulture));
                    foreach (string c in columns) sb.Append(',').Append(line[c]?.ToString(CultureInfo.InvariantCulture));
                    sb.Append('\n');
                }
            }
            File.WriteAllText(file, sb.ToString());
        }
    }
}
