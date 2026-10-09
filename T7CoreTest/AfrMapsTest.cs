using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using T7;

namespace T7CoreTest
{
    [TestClass]
    public class AfrMapsTest
    {
        private static string Here([CallerFilePath] string path = "") => Path.GetDirectoryName(path);

        [TestMethod]
        public void WidebandFeedbackAndImport()
        {
            string dir = Directory.CreateTempSubdirectory("t7afr").FullName;
            string before = SettingsKey.BaseFolder;
            SettingsKey.BaseFolder = dir;
            try
            {
                var settings = new AppSettings(new T7SuiteRegistry());
                Assert.AreEqual(7.39, WidebandAfr.AdcToAfr(0, settings), 1e-9);
                Assert.AreEqual(22.3, WidebandAfr.AdcToAfr(1023, settings), 1e-9);

                string file = Path.Combine(dir, "afr.bin");
                File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), file);
                T7Binary bin = T7Binary.Open(file, 0, false);
                var afr = new AfrFeedback(bin);
                Assert.IsTrue(File.Exists(afr.TargetFile));          // the default target map was created
                Assert.AreEqual(147, afr.Target[1]);                  // 14.7 ×10 in the low airmass corner

                int[] rpm = bin.GetYaxisValues("BFuelCal.Map"), air = bin.GetXaxisValues("BFuelCal.Map");
                Assert.IsFalse(afr.Add(12, false, 500, air[2], 0));   // idle-ish rpm, dropped
                Assert.IsFalse(afr.Add(12, false, rpm[5], air[2], 1)); // fuel cut, dropped
                Assert.IsTrue(afr.Add(12, false, rpm[5], air[2], 0));
                Assert.IsTrue(afr.Add(13, false, rpm[5], air[2], 0));
                int cell = 5 * AfrFeedback.Columns + 2;
                Assert.AreEqual(2, afr.Counter[cell * 2] << 8 | afr.Counter[cell * 2 + 1]);
                Assert.AreEqual(125, afr.Feedback[cell * 2] << 8 | afr.Feedback[cell * 2 + 1]);

                // rich against 14.7: the fuel cell comes down by the error, the others stay
                byte[] fuel = bin.ReadSymbol(bin.FindAny("BFuelCal.Map")!);
                byte[] tuned = AfrFeedback.ApplyFeedback(fuel, afr.Target, afr.Feedback, afr.Counter, false);
                double tgt = (afr.Target[cell * 2] << 8 | afr.Target[cell * 2 + 1]) / 10.0;
                Assert.AreEqual((byte)System.Math.Round(fuel[cell] * (100 - (tgt - 12.5) / tgt * 100) / 100), tuned[cell]);
                Assert.AreEqual(fuel.Length - 1, fuel.Zip(tuned).Count(p => p.First == p.Second));

                // saved and read back, also when the file has comma decimals (T7Suite on a Swedish Windows)
                afr.Save();
                string fb = Path.Combine(dir, "AFRMaps", "afr-AFRFeedbackmap.afr");
                File.WriteAllText(fb, File.ReadAllText(fb).Replace('.', ','));
                var again = new AfrFeedback(bin);
                Assert.AreEqual(125, again.Feedback[cell * 2] << 8 | again.Feedback[cell * 2 + 1]);
                again.Clear();
                Assert.IsTrue(again.Counter.All(b => b == 0));

                // autotune's accept: the chosen cells moved by their percentage
                byte[] accepted = Autotune.Accept([100, 100, 100], [10, -5, 20], [0, 1]);
                CollectionAssert.AreEqual(new byte[] { 110, 95, 100 }, accepted);
            }
            finally
            {
                SettingsKey.BaseFolder = before;
                Directory.Delete(dir, true);
            }
        }
    }
}
