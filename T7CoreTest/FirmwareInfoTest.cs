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

        private static T7Binary Copy(string dir, string name)
        {
            string file = Path.Combine(dir, name);
            File.Copy(Path.Combine(Here(), "..", "T7Binaries", name), file);
            return T7Binary.Open(file, 0, false);
        }

        [TestMethod]
        public void ApplyTogglesOptionsAndFooter()
        {
            string dir = Directory.CreateTempSubdirectory("t7fwedit").FullName;
            try
            {
                T7Binary bin = Copy(dir, "5385356.bin");
                FirmwareInfo before = FirmwareInfo.Read(bin);
                Assert.IsTrue(before.FastThrottleResponse.Available && before.CatalystLightOff.Available && before.OBDII.Available);

                FirmwareEdit edit = FirmwareEdit.From(before);
                edit.TorqueLimiters = !before.TorqueLimiters.Enabled;
                edit.CatalystLightOff = !before.CatalystLightOff.Enabled;
                edit.FastThrottleResponse = !before.FastThrottleResponse.Enabled;
                edit.ExtraFastThrottleResponse = false;
                edit.SecondLambda = !before.SecondLambda.Enabled;
                edit.ChassisID = "YS3XX";                    // shorter: padded to the field
                var log = new CommonSuite.TrionicTransactionLog();
                log.OpenTransActionLog(dir, "proj");
                int asked = 0;
                FirmwareInfo.Apply(bin, edit, false, false, _ => { asked++; return true; }, log);

                FirmwareInfo after = FirmwareInfo.Read(T7Binary.Open(bin.FileName, 0, false));
                Assert.AreEqual(edit.TorqueLimiters, after.TorqueLimiters.Enabled);
                Assert.AreEqual(edit.CatalystLightOff, after.CatalystLightOff.Enabled);
                Assert.AreEqual(edit.FastThrottleResponse, after.FastThrottleResponse.Enabled);
                Assert.AreEqual(edit.SecondLambda, after.SecondLambda.Enabled);
                Assert.AreEqual("YS3XX", after.ChassisID.Trim());
                Assert.AreEqual(before.ChassisID.Length, after.ChassisID.Length);
                Assert.AreEqual(TrionicCANLib.Checksum.ChecksumResult.Ok, TrionicCANLib.Checksum.ChecksumT7.VerifyChecksum(bin.FileName, false, false, (_, _, _) => false));
                Assert.IsGreaterThan(0, log.TransCollection.Count);
                Assert.AreEqual(0, asked);

                // nothing changed: nothing written besides the footer and checksum
                byte[] snapshot = File.ReadAllBytes(bin.FileName);
                FirmwareInfo.Apply(T7Binary.Open(bin.FileName, 0, false), FirmwareEdit.From(after), false, false, _ => true);
                CollectionAssert.AreEqual(snapshot, File.ReadAllBytes(bin.FileName));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        // catalyst light-off on 5385356 changes the FW checksum inside the FB range: one checksum pass left FB stale
        [TestMethod]
        public void EveryToggleKeepsTheChecksum()
        {
            string dir = Directory.CreateTempSubdirectory("t7fwsum").FullName;
            try
            {
                foreach (string name in new[] { "5385356.bin", "5385570.bin", "5168646.bin" })
                    foreach (string option in new[] { "torque", "light", "fast", "lambda", "obd" })
                    {
                        string file = Path.Combine(dir, name);
                        File.Copy(Path.Combine(Here(), "..", "T7Binaries", name), file, true);
                        T7Binary bin = T7Binary.Open(file, 0, false);
                        FirmwareEdit edit = FirmwareEdit.From(FirmwareInfo.Read(bin));
                        switch (option)
                        {
                            case "torque": edit.TorqueLimiters = !edit.TorqueLimiters; break;
                            case "light": edit.CatalystLightOff = !edit.CatalystLightOff; break;
                            case "fast": edit.FastThrottleResponse = !edit.FastThrottleResponse; edit.ExtraFastThrottleResponse = false; break;
                            case "lambda": edit.SecondLambda = !edit.SecondLambda; break;
                            case "obd": edit.OBDII = !edit.OBDII; break;
                        }
                        FirmwareInfo.Apply(bin, edit, false, false, _ => true);
                        Assert.AreEqual(TrionicCANLib.Checksum.ChecksumResult.Ok,
                            TrionicCANLib.Checksum.ChecksumT7.VerifyChecksum(file, false, false, (_, _, _) => false), $"{name} {option}");
                    }
            }
            finally
            {
                Directory.Delete(dir, true);
            }
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
