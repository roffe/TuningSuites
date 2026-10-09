using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using T7;

namespace T7CoreTest
{
    [TestClass]
    public class FirmwareInfoTest
    {
        private static string Here([CallerFilePath] string path = "") => Path.GetDirectoryName(path);

        [TestMethod]
        public void ClosedIndicatorIsTheFirstYesOrNo()
        {
            Assert.AreEqual((3, false), FirmwareInfo.ClosedIndicator(Encoding.ASCII.GetBytes("abcYes No.")));
            Assert.AreEqual((1, true), FirmwareInfo.ClosedIndicator(Encoding.ASCII.GetBytes("xNo\0Yes")));
            // like the old scanner, a failed match doesn't look at the failing byte again: "NNo." is missed
            Assert.AreEqual((0, false), FirmwareInfo.ClosedIndicator(Encoding.ASCII.GetBytes("NNo.")));
        }

        [TestMethod]
        public void StockBin()
        {
            string dir = Directory.CreateTempSubdirectory("t7fw").FullName;
            try
            {
                string file = Path.Combine(dir, "5168646.bin");
                File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), file);
                FirmwareInfo fw = FirmwareInfo.Read(T7Binary.Open(file, 0, false));
                Assert.AreEqual("5168646", fw.PartNumber.Trim());
                Assert.AreEqual("EA1WF0LC.47D", fw.SoftwareVersion.Trim());
                Assert.StartsWith("9-5", fw.CarDescription.Trim());
                Assert.AreEqual("YS300000000000000", fw.ChassisID.Trim());
                Assert.IsTrue(fw.ChecksumEnabled);
                Assert.IsFalse(fw.NoSymbolTable);
                Assert.IsTrue(fw.TorqueLimiters.Available);
                Assert.IsFalse(fw.DisableStartScreen.Available);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
