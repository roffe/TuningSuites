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
using T7;
using TrionicCANLib.API;
using TrionicCANLib.Checksum;

namespace T7CoreTest
{
    /// <summary>
    /// Parses every stock bin in T7Binaries/ and compares a hash of what came out (symbols, addresses, lengths, axes,
    /// descriptions, header fields, checksum) against golden.txt, so refactoring the lifted T7Suite logic can't change
    /// results unnoticed.
    ///   T7_GOLDEN_UPDATE=1      rewrite golden.txt from the current code
    ///   T7_GOLDEN_DUMP=&lt;dir&gt;   also write the readable dump of every bin there, to diff two versions
    /// </summary>
    [TestClass]
    public class BinGoldenTest
    {
        private static string Here([CallerFilePath] string path = "") => Path.GetDirectoryName(path);

        private static readonly string BinDir = Path.Combine(Here(), "..", "T7Binaries");
        private static readonly string GoldenFile = Path.Combine(Here(), "golden.txt");

        [TestMethod]
        public void AllBinsMatchGolden()
        {
            // answer the "load the known symbol list?" prompt with yes so the EU0AF01C / EU09F01C XML path is covered
            UserPrompt.YesNo = (text, caption) => true;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string dumpDir = Environment.GetEnvironmentVariable("T7_GOLDEN_DUMP");
            var actual = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (string bin in Directory.GetFiles(BinDir, "*.bin"))
            {
                string dump = Dump(bin);
                actual[Path.GetFileName(bin)] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dump)))[..16];
                if (!string.IsNullOrEmpty(dumpDir))
                {
                    Directory.CreateDirectory(dumpDir);
                    File.WriteAllText(Path.Combine(dumpDir, Path.GetFileName(bin) + ".txt"), dump);
                }
            }
            Assert.IsGreaterThan(200, actual.Count, "T7Binaries/ is missing");

            if (Environment.GetEnvironmentVariable("T7_GOLDEN_UPDATE") == "1")
            {
                File.WriteAllLines(GoldenFile, actual.Select(kv => kv.Key + " " + kv.Value));
                return;
            }

            var golden = File.ReadAllLines(GoldenFile).Select(l => l.Split(' ')).ToDictionary(p => p[0], p => p[1]);
            var changed = actual.Where(kv => !golden.TryGetValue(kv.Key, out string g) || g != kv.Value).Select(kv => kv.Key).ToList();
            changed.AddRange(golden.Keys.Where(k => !actual.ContainsKey(k)));
            Assert.IsEmpty(changed, "parse output changed for: " + string.Join(", ", changed) +
                ". Diff with T7_GOLDEN_DUMP=<dir> against the previous commit, or accept with T7_GOLDEN_UPDATE=1.");
        }

        private static string Dump(string bin)
        {
            // ExtractFile writes <bin>.xml next to the bin and picks up any <bin>*.xml there, so each bin gets its own folder
            string dir = Directory.CreateTempSubdirectory("t7golden").FullName;
            try
            {
                string file = Path.Combine(dir, Path.GetFileName(bin));
                File.Copy(bin, file);

                var sb = new StringBuilder();
                var header = new T7FileHeader();
                header.init(file, false);
                sb.Append($"partnumber={header.getPartNumber()}|software={header.getSoftwareVersion()}|car={header.getCarDescription()}|chassis={header.getChassisID()}|immo={header.getImmobilizerID()}").Append('\n');
                sb.Append("checksum=" + ChecksumT7.VerifyChecksum(file, false, false, null)).Append('\n');

                SymbolCollection symbols = new Trionic7File().ExtractFile(file, 0, header.getSoftwareVersion());
                sb.Append($"symbols={symbols.Count}|open={Trionic7File.IsSoftwareOpen(symbols)}").Append('\n');

                var axes = new SymbolAxesTranslator();
                foreach (SymbolHelper sh in symbols)
                {
                    axes.GetAxisSymbols(sh.Varname, out string x, out string y, out string xd, out string yd, out string zd);
                    // '\n' not AppendLine, the hashes must match on Windows too
                    sb.Append(string.Join("|", sh.Symbol_number, sh.Varname, sh.Userdescription, sh.Flash_start_address.ToString("X6"),
                        sh.Start_address.ToString("X6"), sh.Length, sh.Internal_address.ToString("X6"), sh.Category, sh.Subcategory,
                        x, y, xd, yd, zd, sh.Description?.ReplaceLineEndings("\\n"))).Append('\n');
                }
                return sb.ToString();
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
