using System.IO;
using System.Linq;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Trionic5Tools;

namespace T5CoreTest
{
    /// <summary>Tune me up ®, stage 4 and higher: the RPM limit is shown in rpm and written to Rpm_max! as rpm / 10, as T5Suite did.</summary>
    [TestClass]
    public class FreeTuneRpmLimitTest
    {
        [TestMethod]
        public void RpmMaxGetsRpmOverTen()
        {
            string dir = Directory.CreateTempSubdirectory("t5freetune").FullName;
            try
            {
                string file = Path.Combine(dir, "4239273.BIN");
                File.Copy(BinGoldenTest.StockBins().Single(b => Path.GetFileName(b) == "4239273.bin"), file);
                T5Binary bin = T5Binary.Open(file);
                Assert.AreEqual(640, bin.File.GetSymbolAsInt("Rpm_max!"));
                var d = T5Tuning.TuneMeUpDefaults(bin)!;
                Assert.AreEqual(6400, d.RpmLimit);

                var (result, _) = T5Tuning.FreeTune(bin, 400, 1.4, true, d.MapSensor, d.Turbo, d.Injectors, d.Valve, 6500, d.KnockTime, true);
                Assert.AreEqual(TuningResult.TuningSuccess, result);
                Assert.AreEqual(650, T5Binary.Open(file).File.GetSymbolAsInt("Rpm_max!"));
            }
            finally { Directory.Delete(dir, true); }
        }
    }
}
