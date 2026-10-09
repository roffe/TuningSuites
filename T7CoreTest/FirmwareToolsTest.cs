using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using T7;

namespace T7CoreTest
{
    [TestClass]
    public class FirmwareToolsTest
    {
        private static string Here([CallerFilePath] string path = "") => Path.GetDirectoryName(path);

        public TestContext TestContext { get; set; } = null!;

        [TestMethod]
        public void EspAndTcmRoundTrip()
        {
            string dir = Directory.CreateTempSubdirectory("t7fw").FullName;
            try
            {
                var bins = Directory.GetFiles(Path.Combine(Here(), "..", "T7Binaries"), "*.bin").OrderBy(f => f).ToList();
                string esp = bins.First(f => FirmwareTools.ReadEsp(T7Binary.Open(f, 0, false)) != null);
                string file = Path.Combine(dir, "esp.bin");
                File.Copy(esp, file);
                T7Binary bin = T7Binary.Open(file, 0, false);
                byte current = FirmwareTools.ReadEsp(bin)!.Value;
                byte other = FirmwareTools.EspCalibrations.Select(c => c.value).First(v => v != current);
                FirmwareTools.WriteEsp(bin, other, false);
                Assert.AreEqual(other, FirmwareTools.ReadEsp(T7Binary.Open(file, 0, false)));

                string? tcmBin = bins.FirstOrDefault(f => FirmwareTools.ReadTcm(T7Binary.Open(f, 0, false)) != null);
                TestContext.WriteLine($"esp: {Path.GetFileName(esp)} {current:X2}, tcm: {Path.GetFileName(tcmBin ?? "none")}");
                if (tcmBin == null) return;
                string tf = Path.Combine(dir, "tcm.bin");
                File.Copy(tcmBin, tf);
                T7Binary tb = T7Binary.Open(tf, 0, false);
                TcmLimit before = FirmwareTools.ReadTcm(tb)!;
                TcmLimit after = before with { ThresholdMod = true, GearMod = false, TorqueLimit = 305 };
                FirmwareTools.WriteTcm(tb, before, after, false);
                TcmLimit back = FirmwareTools.ReadTcm(T7Binary.Open(tf, 0, false))!;
                Assert.IsTrue(back.ThresholdMod);
                Assert.AreEqual(305, back.TorqueLimit);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
