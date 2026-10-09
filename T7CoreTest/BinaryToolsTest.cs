using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using T7;
using TrionicCANLib.Checksum;

namespace T7CoreTest
{
    [TestClass]
    public class BinaryToolsTest
    {
        private static string Here([CallerFilePath] string path = "") => Path.GetDirectoryName(path);

        [TestMethod]
        public void BackupAddressTableAndAxes()
        {
            string dir = Directory.CreateTempSubdirectory("t7tools").FullName;
            try
            {
                string a = Path.Combine(dir, "a.bin"), b = Path.Combine(dir, "b.bin");
                File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), a);
                File.Copy(a, b);
                T7Binary bin = T7Binary.Open(a, 0, false);

                string backup = BinaryTools.Backup(bin, null!);
                StringAssert.EndsWith(backup, ".binarybackup");
                CollectionAssert.AreEqual(File.ReadAllBytes(a), File.ReadAllBytes(backup));

                Assert.IsGreaterThan(0x30000, BinaryTools.AddressTableOffset(File.ReadAllBytes(a)));
                // copied into another file (here a copy): the records land at the same place and the checksum still verifies
                int records = BinaryTools.CopyAddressTable(a, b, false);
                Assert.IsGreaterThan(0, records);
                CollectionAssert.AreEqual(File.ReadAllBytes(a), File.ReadAllBytes(b));
                Assert.AreEqual(ChecksumResult.Ok, T7Binary.OpenRaw(b).VerifyChecksum());

                var axes = BinaryTools.Axes(bin);
                var ign = axes.Single(r => r.Symbol == "IgnNormCal.Map");
                Assert.AreEqual("IgnNormCal.m_AirXSP", ign.XAxis);
                Assert.HasCount(1, BinaryTools.Axes(bin, "IgnNormCal.Map"));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
