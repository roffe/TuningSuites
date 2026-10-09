using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using T7;
using TrionicCANLib.Checksum;

namespace T7CoreTest
{
    [TestClass]
    public class T7CompareTest
    {
        private static string Here([CallerFilePath] string path = "") => Path.GetDirectoryName(path);

        [TestMethod]
        public void CompareDifferenceMapAndTransfer()
        {
            string dir = Directory.CreateTempSubdirectory("t7cmp").FullName;
            try
            {
                string a = Path.Combine(dir, "a.bin"), b = Path.Combine(dir, "b.bin");
                File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), a);
                File.Copy(a, b);
                T7Binary binA = T7Binary.Open(a, 0, false);
                Assert.IsEmpty(T7Compare.Compare(binA, T7Binary.Open(b, 0, false), 0));

                // change every value of the ignition map in b
                T7Binary binB = T7Binary.Open(b, 0, false);
                SymbolHelper map = binB.Find("IgnNormCal.Map");
                byte[] data = binB.ReadSymbol(map);
                byte[] changed = data.Select((x, i) => i % 2 == 1 ? (byte)(x + 1) : x).ToArray();
                binB.WriteSymbol(binB.FileAddress(map), changed, false);

                List<CompareRow> rows = T7Compare.Compare(binA, T7Binary.Open(b, 0, false), 0);
                CompareRow row = rows.Single(r => r.SymbolName == "IgnNormCal.Map");
                Assert.AreEqual(288, row.Differences);          // every 16-bit value of the 18x16 map
                Assert.AreEqual(100, row.Percentage);           // over values (T7Suite: over bytes, halved, which showed 25)
                Assert.AreEqual("IgnNormCal", row.Category);
                Assert.IsFalse(row.MissingInOriFile || row.MissingInCompareFile);

                byte[] diff = T7Compare.DifferenceMap(changed, data, true)!;
                Assert.IsTrue(MapControlsFree.Decode16(diff).All(v => v <= 1));
                Assert.IsNull(T7Compare.DifferenceMap(changed, data[..10], true));

                // transfer the map back from a into b: equal again, checksum valid, a report line
                List<string> report = T7Compare.TransferMaps(binA, b, new HashSet<string> { "IgnNormCal.Map" }, 0, false);
                CollectionAssert.Contains(report, "Transferred symbol IgnNormCal.Map successfully");
                CollectionAssert.AreEqual(data, T7Binary.Open(b, 0, false).ReadSymbol(map));
                Assert.AreEqual(ChecksumResult.Ok, T7Binary.OpenRaw(b).VerifyChecksum());
                Assert.IsTrue(Directory.GetFiles(dir, "*beforetransferringmaps.bin").Length == 1);
                Assert.IsNotEmpty(T7Compare.TransferCandidates(binA));

                Assert.IsNotEmpty(T7Compare.BinaryDiff(a, Path.Combine(Here(), "..", "T7Binaries", "5385356.bin")));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [TestMethod]
        public void DifferentSoftwareListsMissingSymbols()
        {
            string dir = Directory.CreateTempSubdirectory("t7cmp2").FullName;
            try
            {
                string a = Path.Combine(dir, "a.bin"), b = Path.Combine(dir, "b.bin");
                File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), a);
                File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5385356.bin"), b);
                List<CompareRow> rows = T7Compare.Compare(T7Binary.Open(a, 0, false), T7Binary.Open(b, 0, false), 0);
                Assert.IsTrue(rows.Any(r => !r.MissingInOriFile && !r.MissingInCompareFile));
                Assert.IsTrue(rows.Where(r => r.MissingInOriFile).All(r => r.Category == "Missing in original"));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }

    internal static class MapControlsFree
    {
        public static IEnumerable<int> Decode16(byte[] b) => Enumerable.Range(0, b.Length / 2).Select(i => b[i * 2] << 8 | b[i * 2 + 1]);
    }
}
