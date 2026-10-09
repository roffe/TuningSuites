using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommonSuite;
using TrionicCANLib.Checksum;

namespace T7
{
    /// <summary>One row of T7Suite's compare results.</summary>
    public record CompareRow(
        string SymbolName, string Description, int LengthBytes, double Percentage, int Differences, double AverageDifference,
        int SymbolNumber1, int SymbolNumber2, string Userdescription, bool MissingInOriFile, bool MissingInCompareFile,
        string Category, long FlashAddress, long SramAddress);

    /// <summary>frmMain.CompareToFile, the difference map and Transfer maps (frmMain 1827-3185).</summary>
    public static class T7Compare
    {
        // Varname, or the user description for "Symbolnumber N" symbols
        private static string Name(SymbolHelper sh) => sh.Varname.StartsWith("Symbolnumber") ? sh.Userdescription : sh.Varname;

        private static bool ShouldCompare(SymbolHelper sh) =>
            sh.Varname is not ("SymbolNames" or "LocalID") && sh.Userdescription is not ("SymbolNames" or "LocalID");

        public static bool IsCalibration(string name) =>
            name.Contains("Cal.") || name.Contains("Cal1.") || name.Contains("Cal2.") || name.Contains("Cal3.") || name.Contains("Cal4.")
            || name.StartsWith("X_Acc") || name.StartsWith("DisplAdap.");

        private static string CategoryOf(SymbolHelper sh)
        {
            foreach (string n in new[] { sh.Varname, sh.Userdescription })
                if (n.Contains('.')) return n[..n.IndexOf('.')];
            return "";
        }

        private static bool InFile(long address) => address > 0 && address < FileT7Length;

        private const int FileT7Length = 0x80000;

        /// <summary>
        /// The three passes: symbols whose bytes differ (or whose lengths differ), calibration symbols only in the other file
        /// ("Missing in original"), and calibration symbols only in the current one ("Missing in compare").
        /// </summary>
        public static List<CompareRow> Compare(T7Binary current, T7Binary other, int language)
        {
            var rows = new List<CompareRow>();
            var currentByName = new Dictionary<string, SymbolHelper>();
            foreach (SymbolHelper sh in current.Symbols)
                if (Name(sh) is { Length: > 0 } n) currentByName.TryAdd(n, sh);

            foreach (SymbolHelper c in other.Symbols)
            {
                string name = Name(c);
                if (name == "" || !currentByName.TryGetValue(name, out SymbolHelper o) || !ShouldCompare(o)) continue;
                long caddr = other.AddressOf(c), oaddr = current.AddressOf(o);
                if (!InFile(caddr) || !InFile(oaddr)) continue;
                byte[] cur = current.Read((int)oaddr, o.Length), comp = other.Read((int)caddr, c.Length);
                int differences = 0;
                double average = 0, percentage = 0;
                if (cur.Length == comp.Length)
                {
                    if (cur.AsSpan().SequenceEqual(comp)) continue;
                    (differences, percentage, average) = Differences(cur, comp, current.IsSixteenBitTable(name));
                }
                rows.Add(new CompareRow(c.Varname, SymbolTranslator.ToHelpText(c.Varname, language), c.Length, percentage, differences, average,
                    o.Symbol_number, c.Symbol_number, o.Userdescription, false, false, CategoryOf(o), caddr, c.Start_address));
            }

            var otherNames = new HashSet<string>(other.Symbols.Cast<SymbolHelper>().Select(Name));
            foreach (SymbolHelper c in other.Symbols)
            {
                string name = Name(c);
                if (IsCalibration(name) && !currentByName.ContainsKey(name))
                    rows.Add(new CompareRow(name, SymbolTranslator.ToHelpText(name, language), c.Length, 0, 0, 0, 0, c.Symbol_number, c.Userdescription,
                        true, false, "Missing in original", c.Flash_start_address, c.Start_address));
            }
            foreach (SymbolHelper o in current.Symbols)
            {
                string name = Name(o);
                if (IsCalibration(name) && !otherNames.Contains(name))
                    rows.Add(new CompareRow(name, SymbolTranslator.ToHelpText(name, language), o.Length, 0, 0, 0, 0, o.Symbol_number, o.Userdescription,
                        false, true, "Missing in compare", o.Flash_start_address, o.Start_address));
            }
            return rows;
        }

        // values, not bytes: T7Suite halved the differing bytes of 16-bit tables (one changed byte showed 0) and took the
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
        public static List<CompareRow> CompareToSram(T7Binary bin, byte[] ram, int language) =>
            SramRows(bin, language, sh => sh.Flash_start_address > 0 && InFile(bin.AddressOf(sh)) ? bin.Read((int)bin.AddressOf(sh), sh.Length) : null, ram);

        /// <summary>Compare SRAM snapshots: the calibration symbols that differ between two snapshots, laid out by the open bin.</summary>
        public static List<CompareRow> CompareSram(T7Binary bin, byte[] ram1, byte[] ram2, int language) =>
            SramRows(bin, language, sh => ReadSram(ram1, sh.Start_address, sh.Length), ram2);

        private static List<CompareRow> SramRows(T7Binary bin, int language, Func<SymbolHelper, byte[]> first, byte[] ram)
        {
            var rows = new List<CompareRow>();
            foreach (SymbolHelper sh in bin.Symbols)
            {
                string name = Name(sh);
                if (sh.Start_address <= 0 || !IsCalibration(name) || first(sh) is not { } a) continue;
                byte[] b = ReadSram(ram, sh.Start_address, sh.Length);
                if (a.AsSpan().SequenceEqual(b)) continue;
                var (differences, percentage, average) = Differences(a, b, bin.IsSixteenBitTable(name));
                rows.Add(new CompareRow(name, SymbolTranslator.ToHelpText(name, language), sh.Length, percentage, differences, average,
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

        /// <summary>frmBinCompare: the 16-byte lines that differ, as "XXXXXX: b0 b1 ..." for each file.</summary>
        public static List<(string current, string other)> BinaryDiff(string currentFile, string otherFile)
        {
            byte[] a = File.ReadAllBytes(currentFile), b = File.ReadAllBytes(otherFile);
            var lines = new List<(string, string)>();
            for (int i = 0; i < a.Length; i += 16)
            {
                int n = Math.Min(16, a.Length - i);
                ReadOnlySpan<byte> la = a.AsSpan(i, n), lb = i < b.Length ? b.AsSpan(i, Math.Min(n, b.Length - i)) : ReadOnlySpan<byte>.Empty;
                if (la.SequenceEqual(lb)) continue;
                lines.Add(($"{i:X6}: {string.Join(' ', la.ToArray().Select(x => x.ToString("X2")))}",
                    $"{i:X6}: {string.Join(' ', lb.ToArray().Select(x => x.ToString("X2")))}"));
            }
            return lines;
        }

        /// <summary>The symbols Transfer maps offers: in the file, a dotted name, not the checksum switch or name tables.</summary>
        public static bool ShouldTransfer(SymbolHelper sh) =>
            (sh.Varname.Contains('.') || sh.Userdescription.Contains('.'))
            && sh.Varname != "MapChkCal.ST_Enable" && sh.Userdescription != "MapChkCal.ST_Enable"
            && ShouldCompare(sh);

        public static List<SymbolHelper> TransferCandidates(T7Binary current) =>
            current.Symbols.Cast<SymbolHelper>()
                .Where(sh => sh.Flash_start_address > 0 && current.SymbolAddress(sh.SmartVarname) < FileT7Length && sh.Length > 0 && ShouldTransfer(sh))
                .OrderBy(sh => sh.Varname).ToList();

        /// <summary>
        /// TransferMapsToNewBinary: backs up the target, copies every selected symbol whose name matches a target symbol of the
        /// same length, updates the target's checksum. Returns the summary lines T7Suite's report showed.
        /// </summary>
        public static List<string> TransferMaps(T7Binary current, string targetFile, ISet<string> selected, int language, bool autoFixFooter, TrionicTransactionLog log = null)
        {
            var report = new List<string>();
            string backup = Path.Combine(Path.GetDirectoryName(targetFile), Path.GetFileNameWithoutExtension(targetFile) + DateTime.Now.ToString("yyyyMMddHHmmss") + "beforetransferringmaps.bin");
            File.Copy(targetFile, backup, true);
            report.Add("Backup created: " + backup);
            report.Add($"Transferring data from {Path.GetFileName(current.FileName)} to {Path.GetFileName(targetFile)}");

            T7Binary target = T7Binary.Open(targetFile, language, autoFixFooter);
            foreach (SymbolHelper t in target.Symbols.Cast<SymbolHelper>().OrderBy(t => t.Flash_start_address))
            {
                long taddr = target.AddressOf(t);
                if (!InFile(taddr) || t.Length >= 0x1000) continue;
                foreach (SymbolHelper s in current.Symbols)
                {
                    if (!ShouldTransfer(s)) continue;
                    bool match = s.Varname == t.Varname || s.Userdescription == t.Varname || t.Userdescription == s.Varname
                        || (s.Userdescription == t.Userdescription && t.Userdescription != "");
                    if (!match) continue;
                    string name = s.Varname;
                    if (name.StartsWith("Symbolnumber"))
                        name = !t.Varname.StartsWith("Symbolnumber") ? t.Varname : t.Userdescription != "" ? t.Userdescription : s.Userdescription;
                    if (!selected.Contains(s.Varname) && !selected.Contains(s.Userdescription)) continue;
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
            target.UpdateChecksum(autoFixFooter);
            return report;
        }

        /// <summary>"Compare to original file": the stock bin with the part number in Binaries next to the executable.</summary>
        public static string OriginalFile(T7Binary current)
        {
            var header = new T7FileHeader();
            header.init(current.FileName, false);
            string dir = Path.Combine(AppContext.BaseDirectory, "Binaries");
            if (!Directory.Exists(dir)) return null;
            string[] files = Directory.GetFiles(dir, header.getPartNumber().Trim() + ".bin", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive });
            return files.Length == 1 ? files[0] : null;
        }
    }
}
