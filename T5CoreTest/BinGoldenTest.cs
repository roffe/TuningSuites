using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Trionic5Tools;

namespace T5CoreTest
{
    /// <summary>
    /// Opens every stock bin in T5Binaries/ (a copy, the T5 file code can write next to the bin) and compares a hash of what came out
    /// (file type, identifiers, checksum, the firmware properties, symbols with their addresses, each map's width, type, factor,
    /// offset and axes) against golden.txt, so refactoring the lifted Trionic5Tools logic can't change results unnoticed.
    ///   T5_GOLDEN_UPDATE=1      rewrite golden.txt from the current code
    ///   T5_GOLDEN_DUMP=&lt;dir&gt;   also write the readable dump of every bin there, to diff two versions
    /// </summary>
    [TestClass]
    public class BinGoldenTest
    {
        private static string Here([CallerFilePath] string path = "") => Path.GetDirectoryName(path);

        private static readonly string BinDir = Path.Combine(Here(), "..", "T5Binaries");
        private static readonly string GoldenFile = Path.Combine(Here(), "golden.txt");

        internal static string[] StockBins() =>
            Directory.GetFiles(BinDir, "*.bin", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive });

        [TestMethod]
        public void AllBinsMatchGolden()
        {
            string dumpDir = Environment.GetEnvironmentVariable("T5_GOLDEN_DUMP");
            string work = Directory.CreateTempSubdirectory("t5golden").FullName;
            var actual = new SortedDictionary<string, string>(StringComparer.Ordinal);
            try
            {
                foreach (string bin in StockBins())
                {
                    string copy = Path.Combine(work, Path.GetFileName(bin));
                    File.Copy(bin, copy);
                    string dump = Dump(copy);
                    actual[Path.GetFileName(bin)] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dump)))[..16];
                    if (!string.IsNullOrEmpty(dumpDir))
                    {
                        Directory.CreateDirectory(dumpDir);
                        File.WriteAllText(Path.Combine(dumpDir, Path.GetFileName(bin) + ".txt"), dump);
                    }
                }
            }
            finally
            {
                Directory.Delete(work, true);
            }
            Assert.HasCount(85, actual, "T5Binaries/ is missing bins");

            if (Environment.GetEnvironmentVariable("T5_GOLDEN_UPDATE") == "1")
            {
                File.WriteAllLines(GoldenFile, actual.Select(kv => kv.Key + " " + kv.Value));
                return;
            }

            var golden = File.ReadAllLines(GoldenFile).Select(l => l.Split(' ')).ToDictionary(p => p[0], p => p[1]);
            var changed = actual.Where(kv => !golden.TryGetValue(kv.Key, out string g) || g != kv.Value).Select(kv => kv.Key).ToList();
            changed.AddRange(golden.Keys.Where(k => !actual.ContainsKey(k)));
            Assert.IsEmpty(changed, "parse output changed for: " + string.Join(", ", changed) +
                ". Diff with T5_GOLDEN_DUMP=<dir> against the previous commit, or accept with T5_GOLDEN_UPDATE=1.");
        }

        private static string F(double d) => d.ToString("R", CultureInfo.InvariantCulture);

        internal static string Dump(string file)
        {
            var t = new Trionic5File();
            t.SelectFile(file);
            Trionic5FileInformation info = t.ParseFile();
            var sb = new StringBuilder();
            sb.Append($"type={t.DetermineFileType()}|length={info.Filelength}|software={t.GetSoftwareVersion()}|partnumber={t.GetPartnumber()}|" +
                $"checksum={t.ValidateChecksum()}|rpmlimit={t.GetHardcodedRPMLimit(file)}|{t.GetHardcodedRPMLimitTwo(file)}|mapsensor={t.GetMapSensorType(true)}").Append('\n');
            Trionic5Properties p = t.GetTrionicProperties();
            foreach (var prop in typeof(Trionic5Properties).GetProperties().OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                if (prop.GetIndexParameters().Length > 0) continue;
                object v;
                try { v = prop.GetValue(p); } catch (Exception e) { v = "!" + e.GetType().Name; }
                if (v is DateTime) continue; // the sync date reads "now" when the file has none
                sb.Append($"prop {prop.Name}={v}").Append('\n');
            }

            SymbolCollection symbols = info.SymbolCollection;
            sb.Append($"symbols={symbols.Count}").Append('\n');
            foreach (SymbolHelper sh in symbols)
            {
                sb.Append(string.Join("|", sh.Symbol_number, sh.Varname, sh.Userdescription, sh.Flash_start_address.ToString("X6"),
                    sh.Start_address.ToString("X6"), sh.Length, sh.Category, sh.Subcategory, sh.Description?.ReplaceLineEndings("\\n"))).Append('\n');
            }
            foreach (SymbolHelper sh in symbols)
            {
                string name = sh.Varname;
                t.GetMapMatrixWitdhByName(name, out int cols, out int rows);
                t.GetMapAxisDescriptions(name, out string xd, out string yd, out string zd);
                sb.Append(string.Join("|", "map " + name, cols, rows, t.IsTableSixteenBits(name), F(t.GetCorrectionFactorForMap(name)), F(t.GetOffsetForMap(name)),
                    xd, yd, zd, string.Join(",", t.GetMapXaxisValues(name) ?? []), string.Join(",", t.GetMapYaxisValues(name) ?? []))).Append('\n');
            }
            return sb.ToString();
        }
    }
}
