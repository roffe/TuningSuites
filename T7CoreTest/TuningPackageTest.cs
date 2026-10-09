using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using T7;
using TrionicCANLib.Checksum;

namespace T7CoreTest
{
    [TestClass]
    public class TuningPackageTest
    {
        private static string Here([CallerFilePath] string path = "") => Path.GetDirectoryName(path);

        [TestMethod]
        public void PatternsMatchLikeT8Suite()
        {
            byte[] data = [0x01, 0x3D, 0x7C, 0x00, 0x10, 0xFF, 0x3D, 0x7C, 0x00, 0x20, 0x00];
            // 3D 7C ? ?, only where FF follows; the replacement keeps the third byte
            var p = TuningPackage.ParsePattern("'Test',{0x3D,0x7C,?,?},{0x4E,0x71,@2,0x99},{{{},{0xFF}},{{},{0xFE}}}", null!);
            Assert.IsNull(p.Error);
            Assert.AreEqual(1, TuningPackage.Apply(p, data));
            CollectionAssert.AreEqual(new byte[] { 0x01, 0x4E, 0x71, 0x00, 0x99, 0xFF, 0x3D, 0x7C, 0x00, 0x20, 0x00 }, data);

            var direct = TuningPackage.ParsePattern("'Poke',{[0x2]},{0xAA,0xBB}", null!);
            Assert.AreEqual(1, TuningPackage.Apply(direct, data));
            Assert.AreEqual(0xAA, data[2]);

            Assert.AreEqual("mismatch in length", TuningPackage.ParsePattern("'X',{0x01},{0x01,0x02},{{{,},{,}}}", null!).Error);
            Assert.AreEqual("@ index out of bounds", TuningPackage.ParsePattern("'X',{0x01},{@3},{{{,},{,}}}", null!).Error);
        }

        [TestMethod]
        public void ImportWritesSymbolsAndReports()
        {
            string dir = Directory.CreateTempSubdirectory("t7pkg").FullName;
            try
            {
                string file = Path.Combine(dir, "pkg.bin");
                File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), file);
                T7Binary bin = T7Binary.Open(file, 0, false);
                var ign = bin.Find("IgnNormCal.Map");
                byte[] mine = Enumerable.Repeat((byte)0x01, ign.Length).ToArray();
                string pkg = Path.Combine(dir, "stage.t7p");
                File.WriteAllText(pkg,
                    "# a comment\n" +
                    $"symbol=IgnNormCal.Map\nlength={ign.Length}\ndata={string.Join(",", mine.Select(b => b.ToString("X2")))},\n" +
                    "symbol=IgnNormCal.Map\nlength=4\ndata=01,02,03,04,\n" +
                    "symbol=No.Such\nlength=1\ndata=00,\n" +
                    "searchreplace='Nothing',{0xDE,0xAD,0xBE,0xEF,0xDE,0xAD},{0x00,0x00,0x00,0x00,0x00,0x00},{{{,},{,}}}\n");
                var results = TuningPackage.Read(pkg, bin).Apply(bin, false);
                CollectionAssert.AreEqual(new[] { true, false, false, false }, results.Select(r => r.Success).ToArray());
                StringAssert.Contains(results[1].Detail, "length 4");
                Assert.AreEqual("Nothing: 0 replacements", results[3].Map);
                CollectionAssert.AreEqual(mine, T7Binary.Open(file, 0, false).ReadSymbol(ign));
                Assert.AreEqual(ChecksumResult.Ok, T7Binary.OpenRaw(file).VerifyChecksum());
                Assert.HasCount(1, Directory.GetFiles(dir, "*-WIZARD.log"));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
