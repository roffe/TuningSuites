using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using T7;

namespace T7CoreTest
{
    [TestClass]
    public class AfrImportTest
    {
        private static string Here([CallerFilePath] string path = "") => Path.GetDirectoryName(path);

        // T7Suite corrected the fuel map from the float maps; the viewer's ×10 bytes are rounded up to 0.1 AFR (0.1 λ)
        [TestMethod]
        public void ImportUsesTheFeedbackAtFullPrecision()
        {
            string dir = Directory.CreateTempSubdirectory("t7afrimport").FullName;
            try
            {
                string file = Path.Combine(dir, "afr.bin");
                File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), file);
                T7Binary bin = T7Binary.Open(file, 0, false);
                var afr = new AfrFeedback(bin);
                afr.SaveTarget(Enumerable.Repeat(new byte[] { 0, 130 }, afr.Size).SelectMany(b => b).ToArray());   // 13.0 everywhere
                int[] rpm = bin.GetYaxisValues("BFuelCal.Map"), air = bin.GetXaxisValues("BFuelCal.Map");
                int cell = 5 * AfrFeedback.Columns + 2;
                byte[] fuel = Enumerable.Repeat((byte)102, afr.Size).ToArray();

                // 13.04 against 13.0 is 0.3% lean: 102 stays (from 13.1 it became 103)
                Assert.IsTrue(afr.Add(13.04, false, rpm[5], air[2], 0));
                Assert.AreEqual(102, afr.ApplyFeedback(fuel, false)[cell]);

                // 12.5 AFR measured as λ 0.850 is 3.8% rich: 98 (from λ 0.9 = 13.23 it went lean to 104)
                afr.Clear();
                Assert.IsTrue(afr.Add(12.5, true, rpm[5], air[2], 0));
                Assert.AreEqual(98, afr.ApplyFeedback(fuel, true)[cell]);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
