using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CommonSuite
{
    /// <summary>One line of an import's result list ("Import results": Map / Result).</summary>
    public sealed record PackageResult(string Map, bool Success, string Detail = "");

    /// <summary>A `searchreplace=` pattern of a tuning package.</summary>
    public sealed class SearchReplace
    {
        public string Name { get; init; } = "";
        public string Error { get; init; }
        /// <summary>The direct form: REPLACE written at this file offset.</summary>
        public int? Address { get; init; }
        public byte[] Search { get; init; } = [];
        /// <summary>True where SEARCH had `?` (any byte).</summary>
        public bool[] Wildcard { get; init; } = [];
        /// <summary>A byte, or -1 - N for `@N` (the N-th matched byte).</summary>
        public int[] Replace { get; init; } = [];
        /// <summary>Byte strings before / after the match; one pair has to fit, an empty one always does.</summary>
        public List<(byte[] head, byte[] tail)> Context { get; init; } = [];
    }

    /// <summary>
    /// .t7p / .t8p tuning packages (frmMain / Form1 ReadTuningPackageFile, ApplyTuningPackage): `symbol=`, `length=`, `data=`
    /// entries, `searchreplace=` patterns and `binaction=` (which neither suite implemented); other lines are ignored.
    /// </summary>
    public sealed class TuningPackage
    {
        public List<(string name, int length, byte[] data)> Symbols { get; } = [];
        public List<SearchReplace> Patterns { get; } = [];
        public List<string> BinActions { get; } = [];

        public static TuningPackage Read(string file, SuiteBinary bin) => Parse(File.ReadLines(file), bin);

        /// <summary>The package's lines (T8Suite's wizard packs decrypt theirs first).</summary>
        public static TuningPackage Parse(IEnumerable<string> lines, SuiteBinary bin)
        {
            var pkg = new TuningPackage();
            string name = "";
            int length = 0;
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.StartsWith("symbol=")) name = line[7..];
                else if (line.StartsWith("length=")) int.TryParse(line[7..], out length);
                else if (line.StartsWith("data="))
                {
                    byte[] data;
                    try
                    {
                        string[] bytes = line[5..].Split(',', StringSplitOptions.RemoveEmptyEntries);
                        data = bytes.Length >= length ? bytes.Take(length).Select(b => Convert.ToByte(b.Trim(), 16)).ToArray() : null;
                    }
                    catch (FormatException)
                    {
                        data = null;
                    }
                    pkg.Symbols.Add((name, length, data));
                }
                else if (line.StartsWith("searchreplace=")) pkg.Patterns.Add(ParsePattern(line[14..], bin));
                else if (line.StartsWith("binaction=")) pkg.BinActions.Add(line[10..]);
            }
            return pkg;
        }

        // ---- search and replace ----

        /// <summary>
        /// `'Name',{SEARCH},{REPLACE},{{{HEAD},{TAIL}},...}` or `'Name',{[0xADDR]},{REPLACE}`. Bytes are hex, `*Symbol` is the
        /// symbol's 4-byte flash address, `?` in SEARCH any byte (as T8Suite; T7Suite crashed on it), `@N` in REPLACE the N-th
        /// matched byte.
        /// </summary>
        public static SearchReplace ParsePattern(string text, SuiteBinary bin)
        {
            int q1 = text.IndexOf('\''), q2 = q1 < 0 ? -1 : text.IndexOf('\'', q1 + 1);
            if (q2 < 0) return new SearchReplace { Name = text.Length > 6 ? text[..6] + "..." : text, Error = "missing name" };
            string name = text[(q1 + 1)..q2];
            string rest = new string(text[(q2 + 1)..].Where(c => !char.IsWhiteSpace(c)).ToArray()).TrimStart(',');
            try
            {
                List<string> groups = Groups(rest);
                if (groups.Count >= 2 && groups[0].StartsWith('[') && groups[0].EndsWith(']'))
                {
                    int address = Convert.ToInt32(groups[0][1..^1], 16);
                    int[] direct = Tokens(groups[1], bin, out _, true);
                    return new SearchReplace { Name = name, Address = address, Replace = direct };
                }
                if (groups.Count < 2) return new SearchReplace { Name = name, Error = "needs a search and a replace" };
                byte[] search = Tokens(groups[0], bin, out bool[] wildcard, false).Select(b => (byte)Math.Max(b, 0)).ToArray();
                int[] replace = Tokens(groups[1], bin, out _, true);
                if (search.Length != replace.Length) return new SearchReplace { Name = name, Error = "mismatch in length" };
                if (replace.Any(r => r < 0 && -1 - r >= search.Length)) return new SearchReplace { Name = name, Error = "@ index out of bounds" };
                var context = new List<(byte[], byte[])>();
                if (groups.Count > 2)
                {
                    foreach (string pair in Groups(groups[2]))
                    {
                        List<string> ht = Groups(pair);
                        byte[] B(int i) => i < ht.Count ? Tokens(ht[i], bin, out _, false).Select(b => (byte)Math.Max(b, 0)).ToArray() : [];
                        context.Add((B(0), B(1)));
                    }
                }
                return new SearchReplace { Name = name, Search = search, Wildcard = wildcard, Replace = replace, Context = context };
            }
            catch (Exception e) when (e is FormatException or OverflowException or ArgumentException)
            {
                return new SearchReplace { Name = name, Error = "invalid pattern" };
            }
        }

        // the top-level {...} groups of a comma separated list
        private static List<string> Groups(string s)
        {
            var groups = new List<string>();
            int depth = 0, start = -1;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '{' && depth++ == 0) start = i + 1;
                else if (s[i] == '}' && --depth == 0) groups.Add(s[start..i]);
                if (depth < 0) throw new FormatException("unbalanced braces");
            }
            if (depth != 0) throw new FormatException("unbalanced braces");
            return groups;
        }

        private static int[] Tokens(string s, SuiteBinary bin, out bool[] wildcard, bool replace)
        {
            var values = new List<int>();
            var wild = new List<bool>();
            foreach (string token in s.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.StartsWith('*'))
                {
                    // T7Suite dropped an unknown symbol silently; here the pattern fails
                    if (bin.Symbols.Cast<SymbolHelper>().FirstOrDefault(sh => sh.SmartVarname == token[1..]) is not { } sh) throw new ArgumentException("unknown symbol " + token[1..]);
                    long a = sh.Flash_start_address;
                    values.AddRange([(int)(a >> 24 & 0xFF), (int)(a >> 16 & 0xFF), (int)(a >> 8 & 0xFF), (int)(a & 0xFF)]);
                    wild.AddRange([false, false, false, false]);
                }
                else if (replace && token.StartsWith('@'))
                {
                    values.Add(-1 - int.Parse(token[1..], CultureInfo.InvariantCulture));
                    wild.Add(false);
                }
                else if (!replace && token == "?")
                {
                    values.Add(0);
                    wild.Add(true);
                }
                else
                {
                    values.Add(Convert.ToByte(token, 16));
                    wild.Add(false);
                }
            }
            wildcard = wild.ToArray();
            return values.ToArray();
        }

        /// <summary>Every match replaced in data, left to right; the number of replacements.</summary>
        public static int Apply(SearchReplace p, byte[] data)
        {
            if (p.Error != null) return 0;
            if (p.Address is { } address)
            {
                if (address < 0 || address + p.Replace.Length > data.Length || p.Replace.Any(r => r < 0)) return 0;
                for (int k = 0; k < p.Replace.Length; k++) data[address + k] = (byte)p.Replace[k];
                return 1;
            }
            int n = p.Search.Length, count = 0;
            if (n == 0) return 0;
            for (int i = 0; i + n <= data.Length; i++)
            {
                bool match = true;
                for (int k = 0; k < n && match; k++) match = p.Wildcard.Length > k && p.Wildcard[k] || data[i + k] == p.Search[k];
                if (!match || !ContextFits(p, data, i)) continue;
                byte[] matched = data[i..(i + n)];
                for (int k = 0; k < n; k++) data[i + k] = p.Replace[k] >= 0 ? (byte)p.Replace[k] : matched[-1 - p.Replace[k]];
                count++;
            }
            return count;
        }

        private static bool ContextFits(SearchReplace p, byte[] data, int i)
        {
            if (p.Context.Count == 0) return true;
            int end = i + p.Search.Length;
            foreach (var (head, tail) in p.Context)
            {
                bool h = head.Length == 0 || i >= head.Length && data.AsSpan(i - head.Length, head.Length).SequenceEqual(head);
                bool t = tail.Length == 0 || end + tail.Length <= data.Length && data.AsSpan(end, tail.Length).SequenceEqual(tail);
                if (h && t) return true;
            }
            return false;
        }

        // ---- apply ----

        /// <summary>
        /// Import tuning package: the symbols (transaction entries in a project), the search and replace patterns, one checksum
        /// update, and `&lt;bin&gt;-yyyyMMddHHmmss-WIZARD.log`. A symbol whose package length differs from the bin's isn't written,
        /// nor MapChkCal.ST_Enable on open software (T7Suite wrote both).
        /// </summary>
        public List<PackageResult> Apply(SuiteBinary bin, TrionicTransactionLog log = null)
        {
            var results = new List<PackageResult>();
            var lines = new List<string>();
            foreach (var (name, length, data) in Symbols)
            {
                SymbolHelper sh = bin.Symbols.Cast<SymbolHelper>().FirstOrDefault(s => s.SmartVarname == name) ?? bin.FindAny(name);
                string problem = sh == null ? "symbol not found"
                    : data == null ? "invalid data"
                    : bin.IsSoftwareOpen && name == "MapChkCal.ST_Enable" ? "not on open software"
                    : length != sh.Length ? $"length {length} doesn't match the binary's {sh.Length}"
                    : bin.FileAddress(sh) is var a && a <= 0 ? "not in the file" : null;
                lines.Add("Updating symbol: " + name + (problem == null ? "" : " FAILED (" + problem + ")"));
                if (problem == null) bin.WriteData(bin.FileAddress(sh), data, log);
                results.Add(new PackageResult(name, problem == null, problem ?? ""));
            }
            foreach (string action in BinActions)
            {
                lines.Add("Applied binaction: " + action);
                results.Add(new PackageResult(action == "" ? "Failed undefined binaction" : "Failed with binaction=" + action, false));
            }
            if (Patterns.Count > 0)
            {
                byte[] file = File.ReadAllBytes(bin.FileName);
                byte[] before = (byte[])file.Clone();
                foreach (SearchReplace p in Patterns)
                {
                    int n = Apply(p, file);
                    string text = p.Error != null ? $"{p.Name} failing due to {p.Error}" : $"{p.Name}: {n} replacements";
                    lines.Add("Applied searchandreplace: " + text);
                    results.Add(new PackageResult(text, n > 0));
                }
                // only the changed runs, so a project's transaction log gets them
                for (int i = 0; i < file.Length; i++)
                {
                    if (file[i] == before[i]) continue;
                    int start = i;
                    while (i < file.Length && file[i] != before[i]) i++;
                    bin.WriteData(start, file[start..i], log, "Search and replace");
                }
            }
            bin.UpdateChecksum();
            string logFile = Path.Combine(Path.GetDirectoryName(bin.FileName) ?? "",
                $"{Path.GetFileNameWithoutExtension(bin.FileName)}-{DateTime.Now:yyyyMMddHHmmss}-WIZARD.log");
            File.WriteAllLines(logFile, lines);
            return results;
        }
    }
}
