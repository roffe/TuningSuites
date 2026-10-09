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
using T8SuitePro;
using TrionicCANLib.Checksum;

namespace T8CoreTest
{
    /// <summary>
    /// Opens every stock bin in T8Binaries/ and compares a hash of what came out (header and flash blocks, checksum, symbols with
    /// their addresses, PID and TEM tables, each map's width, type, factor and axes, the names map detection would give) against
    /// golden.txt, so refactoring the lifted T8Suite logic can't change results unnoticed.
    ///   T8_GOLDEN_UPDATE=1      rewrite golden.txt from the current code
    ///   T8_GOLDEN_DUMP=&lt;dir&gt;   also write the readable dump of every bin there, to diff two versions
    /// </summary>
    [TestClass]
    public class BinGoldenTest
    {
        private static string Here([CallerFilePath] string path = "") => Path.GetDirectoryName(path);

        private static readonly string BinDir = Path.Combine(Here(), "..", "T8Binaries");
        private static readonly string GoldenFile = Path.Combine(Here(), "golden.txt");

        /// <summary>69 of the 72 stock bins end in .BIN: "*.bin" alone finds 3 of them on Linux.</summary>
        internal static string[] StockBins() =>
            Directory.GetFiles(BinDir, "*.bin", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive });

        [TestMethod]
        public void AllBinsMatchGolden()
        {
            string dumpDir = Environment.GetEnvironmentVariable("T8_GOLDEN_DUMP");
            var actual = new SortedDictionary<string, string>(StringComparer.Ordinal);
            int named = 0;
            // in parallel: nothing is shared between two open files any more (T8Suite kept the open file's state in statics)
            foreach (var (bin, dump, hasNames) in StockBins().AsParallel().Select(bin => (bin, Dump(bin, out bool hasNames), hasNames)).ToList())
            {
                if (hasNames) named++;
                actual[Path.GetFileName(bin)] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dump)))[..16];
                if (!string.IsNullOrEmpty(dumpDir))
                {
                    Directory.CreateDirectory(dumpDir);
                    File.WriteAllText(Path.Combine(dumpDir, Path.GetFileName(bin) + ".txt"), dump);
                }
            }
            Assert.HasCount(72, actual, "T8Binaries/ is missing bins");
            // 27 bins carry their names in an encrypted zip; losing them (old SharpZipLib, code page 850) must not become the baseline
            Assert.AreEqual(71, named, "bins whose symbol names were read");

            if (Environment.GetEnvironmentVariable("T8_GOLDEN_UPDATE") == "1")
            {
                File.WriteAllLines(GoldenFile, actual.Select(kv => kv.Key + " " + kv.Value));
                return;
            }

            var golden = File.ReadAllLines(GoldenFile).Select(l => l.Split(' ')).ToDictionary(p => p[0], p => p[1]);
            var changed = actual.Where(kv => !golden.TryGetValue(kv.Key, out string g) || g != kv.Value).Select(kv => kv.Key).ToList();
            changed.AddRange(golden.Keys.Where(k => !actual.ContainsKey(k)));
            Assert.IsEmpty(changed, "parse output changed for: " + string.Join(", ", changed) +
                ". Diff with T8_GOLDEN_DUMP=<dir> against the previous commit, or accept with T8_GOLDEN_UPDATE=1.");
        }

        private static string Dump(string file, out bool hasNames)
        {
            // an empty folder: only <bin>.xml next to the bin could add names, and the stock bins have none
            T8Binary b = T8Binary.Open(file, false, Path.Combine(Path.GetTempPath(), "t8golden-no-symbol-lists"));
            SymbolCollection symbols = b.Symbols;
            var sb = new StringBuilder();
            T8Header header = b.Header;
            sb.Append($"software={header.SoftwareVersion}|partnumber={header.PartNumber}|hardware={header.HardwareID}|device={header.DeviceType}|" +
                $"released={header.ReleaseDate}|programmer={header.ProgrammerName}|station={header.ProgrammerDevice}|serial={header.SerialNumber}|" +
                $"chassis={header.ChassisID}|ecu={header.EcuDescription}|interface={header.InterfaceDevice}|blocks={header.NumberOfFlashBlocks}").Append('\n');
            foreach (FlashBlock fb in header.FlashBlocks)
            {
                fb.DecodeBlock(out string vin, out string ecu, out string itf, out string secret);
                sb.Append($"block {fb.BlockNumber}|{fb.BlockType}|{fb.BlockAddress:X8}|{vin}|{ecu}|{itf}|{secret}").Append('\n');
            }
            sb.Append($"checksum={ChecksumT8.VerifyChecksum(file, false, (_, _, _) => false)}|area={ChecksumT8.GetChecksumAreaOffset(file):X8}").Append('\n');

            sb.Append($"symbols={symbols.Count}|open={b.IsSoftwareOpen}|xml={b.SymbolListLoaded}").Append('\n');
            hasNames = symbols.Cast<SymbolHelper>().Count(sh => !sh.Varname.StartsWith("Symbolnumber ")) > 1000;
            foreach (SymbolHelper sh in symbols)
            {
                // '\n' not AppendLine, the hashes must match on Windows too
                sb.Append(string.Join("|", sh.Symbol_number, sh.Symbol_number_ECU, sh.Varname, sh.Userdescription, sh.Internal_address.ToString("X6"),
                    sh.Flash_start_address.ToString("X6"), sh.Start_address.ToString("X6"), sh.Length, sh.BitMask.ToString("X4"), sh.Symbol_type.ToString("X2"),
                    sh.Symbol_extendedtype.ToString("X2"), sh.Category, sh.Subcategory, sh.Description?.ReplaceLineEndings("\\n"))).Append('\n');
            }
            foreach (PidHelper ph in b.Pids?.Cast<PidHelper>() ?? [])
                sb.Append($"pid {ph.Index}|{ph.FileAddress:X6}|{ph.PID}|{ph.SymbolIndex}|{ph.PackedFlags:X2}").Append('\n');
            foreach (PidHelper ph in b.Tems?.Cast<PidHelper>() ?? [])
                sb.Append($"tem {ph.Index}|{ph.FileAddress:X6}|{ph.PID}|{ph.SymbolIndex}").Append('\n');

            // what a map viewer gets for every symbol in the file
            sb.Append($"offsets={b.AddressOffset:X6}|{b.SecondaryOffset:X6}").Append('\n');
            foreach (SymbolHelper sh in symbols)
            {
                if (b.FileAddress(sh) < 0) continue;
                string name = sh.SmartVarname;
                var (x, y, xd, yd, zd) = b.AxisSymbols(name);
                sb.Append(string.Join("|", "map " + name, b.TableWidth(name), b.IsSixteenBitTable(name),
                    b.GetMapCorrectionFactor(name).ToString("R", CultureInfo.InvariantCulture), x, y, xd, yd, zd,
                    string.Join(",", b.GetXaxisValues(name)), string.Join(",", b.GetYaxisValues(name)))).Append('\n');
            }

            // what "Auto mapdetection active" would name (it only fills empty user descriptions, by symbol number)
            var before = symbols.Cast<SymbolHelper>().ToDictionary(sh => sh.Symbol_number, sh => sh.Userdescription);
            new SymbolFiller().CheckAndFillCollection(symbols);
            foreach (SymbolHelper sh in symbols.Cast<SymbolHelper>().Where(sh => sh.Userdescription != before[sh.Symbol_number]).OrderBy(sh => sh.Symbol_number))
                sb.Append($"filler {sh.Symbol_number}|{sh.Userdescription}|{sh.Description?.ReplaceLineEndings("\\n")}").Append('\n');
            return sb.ToString();
        }
    }
}
