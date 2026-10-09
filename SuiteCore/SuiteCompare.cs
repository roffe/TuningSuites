using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CommonSuite
{
    /// <summary>One row of the compare results.</summary>
    public record CompareRow(
        string SymbolName, string Description, int LengthBytes, double Percentage, int Differences, double AverageDifference,
        int SymbolNumber1, int SymbolNumber2, string Userdescription, bool MissingInOriFile, bool MissingInCompareFile,
        string Category, long FlashAddress, long SramAddress);

    /// <summary>
    /// CompareToFile, the difference map, the binary compares and Transfer maps (T7Suite frmMain 1827-3185, T8Suite Form1 3323,
    /// 3806, 3891, 5544-5631). How symbols are named, addressed and described comes from the binaries.
    /// </summary>
    public static class SuiteCompare
    {
        private static string CategoryOf(SymbolHelper sh)
        {
            foreach (string n in new[] { sh.Varname, sh.Userdescription })
                if (n.Contains('.')) return n[..n.IndexOf('.')];
            return "";
        }

        private static bool InFile(SuiteBinary bin, long address) => address > 0 && address < bin.FileLength;

        /// <summary>
        /// The three passes: symbols whose bytes differ (or whose lengths differ), calibration symbols only in the other file
        /// ("Missing in original"), and calibration symbols only in the current one ("Missing in compare").
        /// </summary>
        public static List<CompareRow> Compare(SuiteBinary current, SuiteBinary other)
        {
            var rows = new List<CompareRow>();
            var currentByName = new Dictionary<string, SymbolHelper>();
            foreach (SymbolHelper sh in current.Symbols)
                if (current.CompareName(sh) is { Length: > 0 } n) currentByName.TryAdd(n, sh);

            foreach (SymbolHelper c in other.Symbols)
            {
                string name = other.CompareName(c);
                if (name == "" || !currentByName.TryGetValue(name, out SymbolHelper o)) continue;
                long caddr = other.AddressOf(c), oaddr = current.AddressOf(o);
                if (!InFile(other, caddr) || !InFile(current, oaddr)) continue;
                byte[] cur = current.Read((int)oaddr, o.Length), comp = other.Read((int)caddr, c.Length);
                int differences = 0;
                double average = 0, percentage = 0;
                if (cur.Length == comp.Length)
                {
                    if (cur.AsSpan().SequenceEqual(comp)) continue;
                    (differences, percentage, average) = Differences(cur, comp, current.IsSixteenBitTable(name));
                }
                rows.Add(new CompareRow(name, current.Describe(name), c.Length, percentage, differences, average,
                    o.Symbol_number, c.Symbol_number, o.Userdescription, false, false, CategoryOf(o), caddr, c.Start_address));
            }

            var otherNames = new HashSet<string>(other.Symbols.Cast<SymbolHelper>().Select(other.CompareName));
            foreach (SymbolHelper c in other.Symbols)
            {
                string name = other.CompareName(c);
                if (current.IsCalibration(name) && !currentByName.ContainsKey(name))
                    rows.Add(new CompareRow(name, current.Describe(name), c.Length, 0, 0, 0, 0, c.Symbol_number, c.Userdescription,
                        true, false, "Missing in original", c.Flash_start_address, c.Start_address));
            }
            foreach (SymbolHelper o in current.Symbols)
            {
                string name = current.CompareName(o);
                if (current.IsCalibration(name) && !otherNames.Contains(name))
                    rows.Add(new CompareRow(name, current.Describe(name), o.Length, 0, 0, 0, 0, o.Symbol_number, o.Userdescription,
                        false, true, "Missing in compare", o.Flash_start_address, o.Start_address));
            }
            return rows;
        }

        // values, not bytes: both suites halved the differing bytes of 16-bit tables (one changed byte showed 0) and took the
        // percentage over bytes (a 16-bit map changed everywhere showed 50)
        private static (int differences, double percentage, double average) Differences(byte[] a, byte[] b, bool sixteenBit)
        {
            int size = sixteenBit ? 2 : 1, values = a.Length / size, differences = 0;
            for (int v = 0; v < values; v++)
                if (!a.AsSpan(v * size, size).SequenceEqual(b.AsSpan(v * size, size))) differences++;
            double average = a.Length == 0 ? 0 : a.Average(x => (double)x) - b.Average(x => (double)x);
            return (differences, values == 0 ? 0 : differences * 100.0 / values, average);
        }

        /// <summary>readdatafromSRAMfile: the bytes at a symbol's SRAM address in a snapshot, the address wrapping at its size.</summary>
        public static byte[] ReadSram(byte[] ram, long address, int length)
        {
            var data = new byte[length];
            if (ram.Length == 0) return data;
            for (int i = 0; i < length; i++) data[i] = ram[(address + i) % ram.Length];
            return data;
        }

        /// <summary>
        /// Compare to SRAM snapshot: the calibration symbols whose bytes in the file differ from the snapshot. T7Suite left the
        /// difference columns at 0; they are filled in here.
        /// </summary>
        public static List<CompareRow> CompareToSram(SuiteBinary bin, byte[] ram) =>
            SramRows(bin, sh => sh.Flash_start_address > 0 && InFile(bin, bin.AddressOf(sh)) ? bin.Read((int)bin.AddressOf(sh), sh.Length) : null, ram);

        /// <summary>Compare SRAM snapshots: the calibration symbols that differ between two snapshots, laid out by the open bin.</summary>
        public static List<CompareRow> CompareSram(SuiteBinary bin, byte[] ram1, byte[] ram2) =>
            SramRows(bin, sh => ReadSram(ram1, sh.Start_address, sh.Length), ram2);

        private static List<CompareRow> SramRows(SuiteBinary bin, Func<SymbolHelper, byte[]> first, byte[] ram)
        {
            var rows = new List<CompareRow>();
            foreach (SymbolHelper sh in bin.Symbols)
            {
                string name = bin.CompareName(sh);
                if (sh.Start_address <= 0 || !bin.IsCalibration(name) || first(sh) is not { } a) continue;
                byte[] b = ReadSram(ram, sh.Start_address, sh.Length);
                if (a.AsSpan().SequenceEqual(b)) continue;
                var (differences, percentage, average) = Differences(a, b, bin.IsSixteenBitTable(name));
                rows.Add(new CompareRow(name, bin.Describe(name), sh.Length, percentage, differences, average,
                    sh.Symbol_number, sh.Symbol_number, sh.Userdescription, false, false, CategoryOf(sh), sh.Flash_start_address, sh.Start_address));
            }
            return rows;
        }

        /// <summary>StartCompareDifferenceViewer: |other - current| per value (unsigned 16-bit pairs or bytes), null when the lengths differ.</summary>
        public static byte[] DifferenceMap(byte[] other, byte[] current, bool sixteenBit)
        {
            if (other.Length != current.Length) return null;
            var diff = new byte[other.Length];
            if (sixteenBit)
            {
                for (int i = 0; i + 1 < other.Length; i += 2)
                {
                    int d = Math.Abs((other[i] << 8 | other[i + 1]) - (current[i] << 8 | current[i + 1]));
                    diff[i] = (byte)(d >> 8);
                    diff[i + 1] = (byte)d;
                }
            }
            else
            {
                for (int i = 0; i < other.Length; i++) diff[i] = (byte)Math.Abs(other[i] - current[i]);
            }
            return diff;
        }

        /// <summary>
        /// frmBinCompare: the 16-byte lines that differ, as "XXXXXX: b0 b1 ..." for each file. With symbols ("Compare binary
        /// outside symbolrange", T8Suite) only lines where a byte outside every symbol's flash range differs.
        /// </summary>
        public static List<(string current, string other)> BinaryDiff(string currentFile, string otherFile, SymbolCollection outsideOf = null)
        {
            byte[] a = File.ReadAllBytes(currentFile), b = File.ReadAllBytes(otherFile);
            var inside = new bool[a.Length];
            foreach (SymbolHelper sh in outsideOf ?? [])
                for (long i = Math.Max(sh.Flash_start_address, 0); i < Math.Min(sh.Flash_start_address + sh.Length, a.Length); i++) inside[i] = true;
            var lines = new List<(string, string)>();
            for (int i = 0; i < a.Length; i += 16)
            {
                int n = Math.Min(16, a.Length - i);
                bool differs = false;
                for (int k = i; k < i + n && !differs; k++) differs = !inside[k] && (k >= b.Length || a[k] != b[k]);
                if (!differs) continue;
                ReadOnlySpan<byte> la = a.AsSpan(i, n), lb = i < b.Length ? b.AsSpan(i, Math.Min(n, b.Length - i)) : ReadOnlySpan<byte>.Empty;
                lines.Add(($"{i:X6}: {string.Join(' ', la.ToArray().Select(x => x.ToString("X2")))}",
                    $"{i:X6}: {string.Join(' ', lb.ToArray().Select(x => x.ToString("X2")))}"));
            }
            return lines;
        }

        /// <summary>The symbols Transfer maps offers: a dotted name, not the checksum switch.</summary>
        private static bool ShouldTransfer(SuiteBinary bin, SymbolHelper sh) => bin.CompareName(sh) is var n && n.Contains('.') && n != "MapChkCal.ST_Enable";

        /// <summary>The names Transfer maps offers: symbols in the file with a length.</summary>
        public static List<string> TransferCandidates(SuiteBinary current) =>
            current.Symbols.Cast<SymbolHelper>()
                .Where(sh => sh.Flash_start_address > 0 && current.AddressOf(sh) < current.FileLength && sh.Length > 0 && ShouldTransfer(current, sh))
                .Select(current.CompareName).Distinct().OrderBy(n => n).ToList();

        /// <summary>
        /// TransferMapsToNewBinary: backs up the target, then copies every selected symbol into the target symbol of that name
        /// when the lengths match, and updates the target's checksum. Returns the summary lines of the old report.
        /// </summary>
        public static List<string> TransferMaps(SuiteBinary current, string targetFile, Func<string, SuiteBinary> open, ISet<string> selected, TrionicTransactionLog log = null)
        {
            var report = new List<string>();
            string backup = Path.Combine(Path.GetDirectoryName(targetFile), Path.GetFileNameWithoutExtension(targetFile) + DateTime.Now.ToString("yyyyMMddHHmmss") + "beforetransferringmaps.bin");
            File.Copy(targetFile, backup, true);
            report.Add("Backup created: " + backup);
            report.Add($"Transferring data from {Path.GetFileName(current.FileName)} to {Path.GetFileName(targetFile)}");

            var sources = current.Symbols.Cast<SymbolHelper>().Where(s => ShouldTransfer(current, s) && selected.Contains(current.CompareName(s))).ToList();
            SuiteBinary target = open(targetFile);
            foreach (SymbolHelper t in target.Symbols.Cast<SymbolHelper>().OrderBy(t => t.Flash_start_address))
            {
                long taddr = target.AddressOf(t);
                if (!InFile(target, taddr) || t.Length >= 0x1000) continue;
                string name = target.CompareName(t);
                foreach (SymbolHelper s in sources.Where(s => current.CompareName(s) == name))
                {
                    if (s.Length != t.Length)
                    {
                        report.Add($"Unable to transfer symbol {name} because source and target lengths don't match!");
                        continue;
                    }
                    try
                    {
                        target.WriteData((int)taddr, current.Read((int)current.AddressOf(s), s.Length), log);
                        report.Add($"Transferred symbol {name} successfully");
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        report.Add($"Failed to transfer symbol {name}: {e.Message}");
                    }
                }
            }
            target.UpdateChecksum();
            return report;
        }
    }
}
