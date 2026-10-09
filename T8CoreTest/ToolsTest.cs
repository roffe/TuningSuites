using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using T8SuitePro;
using TrionicCANLib.Checksum;

namespace T8CoreTest
{
    /// <summary>The T8 tools: PID / TEM write-back, disassembly, interrupt vectors, axis rows.</summary>
    [TestClass]
    public class ToolsTest
    {
        private const string Old = "55353231_FA56_C_FME2_37_FIEF_81c.BIN";

        private static string NoSymbolLists => Path.Combine(Path.GetTempPath(), "t8golden-no-symbol-lists");

        private static T8Binary OpenCopy(string dir)
        {
            string file = Path.Combine(dir, Old);
            File.Copy(BinGoldenTest.StockBins().Single(b => Path.GetFileName(b) == Old), file);
            return T8Binary.Open(file, false, NoSymbolLists);
        }

        [TestMethod]
        public void PidAndTemWriteBack()
        {
            string dir = Directory.CreateTempSubdirectory("t8pid").FullName;
            try
            {
                T8Binary bin = OpenCopy(dir);
                PidCollection pids = bin.Pids.CopyOf();
                PidHelper first = pids[0];
                byte pad = bin.Read(first.FileAddress + 7, 1)[0];
                first.PID = "ABCD";
                first.SymbolIndex = 0x1234;
                first.WriteFlag = 1;
                bin.WritePids(pids);
                CollectionAssert.AreEqual(new byte[] { 0xAB, 0xCD, 0, 0, 0x12, 0x34, first.PackedFlags, pad }, bin.Read(first.FileAddress, 8));
                Assert.AreSame(pids, bin.Pids);
                // T8Suite left the checksum to UpdateChecksum afterwards
                Assert.AreNotEqual(ChecksumResult.Ok, bin.VerifyChecksum());
                bin.UpdateChecksum();

                // a row without a symbol or with an unreadable PID isn't written
                byte[] before = bin.Read(pids[1].FileAddress, 7);
                pids[1].SymbolIndex = 0;
                pids[2].PID = "XYZ";
                byte[] before2 = bin.Read(pids[2].FileAddress, 7);
                bin.WritePids(pids);
                CollectionAssert.AreEqual(before, bin.Read(pids[1].FileAddress, 7));
                CollectionAssert.AreEqual(before2, bin.Read(pids[2].FileAddress, 7));

                // TEM rows (no stock bin has a table): [index][label zero padded]; "OFF" with index 0 stays
                int at = pids[10].FileAddress;
                byte[] off = bin.Read(at, 6);
                var tems = new PidCollection
                {
                    new PidHelper { FileAddress = at, PID = "OFF", IsProtected = true },
                    new PidHelper { FileAddress = at + 6, PID = "AB", SymbolIndex = 0x0102 },
                };
                bin.WriteTems(tems);
                CollectionAssert.AreEqual(off, bin.Read(at, 6));
                CollectionAssert.AreEqual(new byte[] { 1, 2, (byte)'A', (byte)'B', 0, 0 }, bin.Read(at + 6, 6));
                Assert.AreSame(tems, bin.Tems);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [TestMethod]
        public void DisassemblyVectorsAndAxes()
        {
            string dir = Directory.CreateTempSubdirectory("t8asm").FullName;
            try
            {
                T8Binary bin = OpenCopy(dir);
                var vectors = bin.InterruptVectors();
                Assert.HasCount(120, vectors);
                Assert.AreEqual("User defined vector 55", vectors[119].Name);

                string asm = Path.Combine(dir, "x.asm");
                bin.Disassemble(asm, false);
                string[] lines = File.ReadAllLines(asm);
                Assert.IsTrue(lines.Any(l => l.StartsWith("0x") && l.Contains('\t')));
                // T8's memory map: flash symbols as ROM_, SRAM as RAM_
                Assert.IsTrue(lines.Any(l => l.Contains("RAM_")));

                string full = Path.Combine(dir, "x_full.asm");
                bin.Disassemble(full, true);
                Assert.IsGreaterThan(100000, File.ReadLines(full).Count());
                Assert.IsFalse(File.ReadAllText(full).Contains(@"\par"));

                var axes = bin.AxisRows();
                var ign = axes.Single(r => r.Symbol == "IgnAbsCal.fi_NormalMAP");
                Assert.AreEqual("IgnAbsCal.m_AirNormXSP", ign.XAxis);
                Assert.AreEqual("IgnAbsCal.n_EngNormYSP", ign.YAxis);
                Assert.HasCount(1, bin.AxisRows("IgnAbsCal.fi_NormalMAP"));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [TestMethod]
        public void BinaryFromTisFile()
        {
            string dir = Directory.CreateTempSubdirectory("t8tis").FullName;
            try
            {
                string baseBin = BinGoldenTest.StockBins().Single(b => Path.GetFileName(b) == Old);
                byte[] program = new byte[5000];
                new System.Random(8).NextBytes(program);
                byte[] key = "9hwmG9"u8.ToArray();
                byte[] gbf = program.Select((b, i) => (byte)(b ^ key[i % 6])).ToArray();
                string raw = Path.Combine(dir, "raw.gbf"), zipped = Path.Combine(dir, "zipped.gbf"), s19 = Path.Combine(dir, "srec.s19");
                File.WriteAllBytes(raw, gbf);
                using (var gz = new System.IO.Compression.GZipStream(File.Create(zipped), System.IO.Compression.CompressionLevel.Optimal))
                    gz.Write(gbf);
                Assert.IsTrue(new CommonSuite.Srecord().ConvertBinToSrec(raw, (ulong)gbf.Length, s19));

                byte[] expected = TisFile.Build(baseBin, raw);
                CollectionAssert.AreEqual(File.ReadAllBytes(baseBin)[..0x20000], expected[..0x20000]);
                CollectionAssert.AreEqual(program, expected[0x20000..(0x20000 + program.Length)]);
                // "T8SuitePro" as the programming station (length 10, id 0x10), then the adaption region flag, encoded (b ^ 0x21) - 0xD6
                byte[] footer = expected[(0x20000 + program.Length)..(0x20000 + program.Length + 15)];
                CollectionAssert.AreEqual("\n\u0010T8SuitePro\u0001\u00F9\u0001".Select(c => (byte)(((byte)c ^ 0x21) - 0xD6)).ToArray(), footer);
                Assert.IsTrue(expected[(0x20000 + program.Length + 15)..].All(b => b == 0xFF));
                // gzipped, or an S19 of it, gives the same; nothing is left next to the TIS file
                CollectionAssert.AreEqual(expected, TisFile.Build(baseBin, zipped));
                CollectionAssert.AreEqual(expected, TisFile.Build(baseBin, s19));
                CollectionAssert.AreEquivalent(new[] { "raw.gbf", "zipped.gbf", "srec.s19" }, Directory.GetFiles(dir).Select(Path.GetFileName).ToArray());
                // without a TIS file: the base's first part, FF after it
                Assert.IsTrue(TisFile.Build(baseBin, null)[0x20000..].All(b => b == 0xFF));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        public TestContext TestContext { get; set; } = null!;

        [TestMethod]
        public void TuningWizardPacks()
        {
            string packs = Path.Combine(Path.GetDirectoryName(BinGoldenTest.StockBins()[0]), "..", "TuningPacks");
            var all = WizardPack.Load(packs);
            foreach (WizardPack p in all)
                TestContext.WriteLine($"{Path.GetFileName(p.FileName)} | {p.Name} | {p.BinType} | {string.Join(",", p.Whitelist)} | {string.Join(",", p.Blacklist)} | {p.Code} | {p.Author} | {p.Message}");
            // the 17 shipped packs, one of them signed over a different text (its source started with a BOM)
            Assert.HasCount(16, all);
            Assert.IsFalse(all.Any(p => Path.GetFileName(p.FileName).StartsWith("220129_")));

            // compatibelSoftware: bintype, whitelist and blacklist against the software version
            WizardPack Pack(string file) => all.Single(p => Path.GetFileName(p.FileName) == file);
            Assert.IsTrue(Pack("B207E_L_Suite_St1_MY03-06.t8x").Compatible("FA56_C_FME2_37_FIEF_81c"));
            Assert.IsTrue(Pack("B207E_L_Suite_St1_MY03-06.t8x").Compatible("FC01_O"));
            Assert.IsFalse(Pack("B207E_L_Suite_St1_MY03-06.t8x").Compatible("FC0G_C_FMEP_63_FIEF_82s"));
            Assert.IsTrue(Pack("FC0G_Torque_Limiter_Removal.t8x").Compatible("FC0G_C_FMEP_63_FIEF_82s"));
            Assert.IsFalse(Pack("FC0G_Torque_Limiter_Removal.t8x").Compatible("FC0J_C_FMEP_63_FIEF_82s"));
            Assert.IsTrue(Pack("MY2007-2011_B207E_to_B207L_BP_conversion.t8x").Compatible("FD0I_C_FMEP_33_FIEF_81l"));
            Assert.IsFalse(Pack("MY2007-2011_B207E_to_B207L_GS_conversion.t8x").Compatible("FD0I_C_FMEP_33_FIEF_81l"));
            Assert.IsTrue(Pack("TD04L_14T_B207EL_Complete.t8x").Compatible("FD0I_C_FMEP_33_FIEF_81l"));

            // applied: a backup, the package, the PI area's programmer and release date; the checksum still verifies
            string dir = Directory.CreateTempSubdirectory("t8wiz").FullName;
            try
            {
                T8Binary bin = OpenCopy(dir);
                byte[] before = File.ReadAllBytes(bin.FileName);
                List<string> results = Pack("550cc_Bosch_Injcorr_387.t8x").Apply(bin, null);
                foreach (string r in results) TestContext.WriteLine(r);
                Assert.IsTrue(results.Any(r => r.StartsWith("OK: InjCorrCal.InjectorConst")));
                Assert.AreEqual("Update PI Area", results[^1]);
                string backup = Directory.GetFiles(dir, "*-BACKUP-BEFORE-WIZARD-Bosch 550cc Injectors.bin").Single();
                CollectionAssert.AreEqual(before, File.ReadAllBytes(backup));
                Assert.AreEqual(ChecksumResult.Ok, bin.VerifyChecksum());
                var info = FirmwareInfo.Read(bin.FileName);
                TestContext.WriteLine(info.ProgrammerName + " / " + info.ReleaseDate);
                StringAssert.StartsWith(info.ProgrammerName, "T8Suite");
                StringAssert.StartsWith(info.ReleaseDate, System.DateTime.Now.ToString("yyyy-MM-dd"));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [TestMethod]
        public void AirmassResult()
        {
            string dir = Directory.CreateTempSubdirectory("t8air").FullName;
            try
            {
                T8Binary bin = OpenCopy(dir);
                Assert.IsTrue(T8AirmassResult.Available(bin));
                // high output, manual, fifth gear: the engine torque table limits WOT everywhere
                var r = new T8AirmassResult(bin, new CommonSuite.AirmassOptions { Variant = true });
                int wot = r.Pedal.Length - 1, c3500 = System.Array.IndexOf(r.Rpm, 3500);
                Assert.AreEqual(808, r.Airmass[wot, c3500]);
                Assert.IsTrue(Enumerable.Range(0, r.Rpm.Length).All(c => r.Limiter[wot, c] == CommonSuite.AirmassLimitType.TorqueLimiterEngine));
                Assert.AreEqual(261, r.Torque(808, 3500));
                Assert.AreEqual(58, r.InjectorDc(808, 3500));
                // the fuel map in 1/128
                Assert.AreEqual(0.88, r.TargetLambda(808, 3500)!.Value, 1e-9);
                Assert.IsNotNull(r.Egt(808, 3500));
                Assert.IsFalse(r.CanE85);
                CollectionAssert.AreEqual(new[] { "TrqLimCal.Trq_MaxEngineManTab1" }, r.LimiterMaps(CommonSuite.AirmassLimitType.TorqueLimiterEngine));
                Assert.IsTrue(Enumerable.Range(0, r.Rpm.Length).All(c => r.Airmass[0, c] <= r.Airmass[wot, c]));

                // automatic: MaxAirmassAu caps the low rpm
                var auto = new T8AirmassResult(bin, new CommonSuite.AirmassOptions { Variant = true, Automatic = true });
                Assert.AreEqual(CommonSuite.AirmassLimitType.AirmassLimiter, auto.Limiter[wot, 0]);
                Assert.AreEqual(600, auto.Airmass[wot, 0]);
                Assert.AreEqual((CommonSuite.CompressorMap.T1752, 2.0), T8AirmassResult.CompressorDefaults(bin.Header.ChassisID));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
