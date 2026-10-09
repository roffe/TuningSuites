using System;
using System.IO;
using System.Linq;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using T8SuitePro;
using TrionicCANLib.Checksum;

namespace T8CoreTest
{
    [TestClass]
    public class T8BinaryTest
    {
        private static string NoSymbolLists => Path.Combine(Path.GetTempPath(), "t8golden-no-symbol-lists");

        private static T8Binary OpenCopy(out string dir)
        {
            dir = Directory.CreateTempSubdirectory("t8binary").FullName;
            string file = Path.Combine(dir, "55353231_FA56_C_FME2_37_FIEF_81c.BIN");
            File.Copy(BinGoldenTest.StockBins().Single(b => Path.GetFileName(b) == "55353231_FA56_C_FME2_37_FIEF_81c.BIN"), file);
            return T8Binary.Open(file, false, NoSymbolLists);
        }

        [TestMethod]
        public void OpensAStockBin()
        {
            T8Binary bin = OpenCopy(out string dir);
            try
            {
                Assert.AreEqual("FA56_C_FME2_37_FIEF_81c", bin.SoftwareVersion.Trim());
                Assert.IsFalse(bin.IsSoftwareOpen);
                Assert.AreEqual(ChecksumResult.Ok, bin.VerifyChecksum());
                Assert.IsGreaterThan(500, bin.Pids.Count);

                SymbolHelper map = bin.Find("IgnAbsCal.fi_NormalMAP");
                Assert.AreEqual(0x0AA5EE, bin.FileAddress(map));
                Assert.HasCount(576, bin.ReadSymbol(map));
                Assert.AreEqual(18, bin.TableWidth("IgnAbsCal.fi_NormalMAP"));
                Assert.IsTrue(bin.IsSixteenBitTable("IgnAbsCal.fi_NormalMAP"));
                Assert.AreEqual(0.1, bin.GetMapCorrectionFactor("IgnAbsCal.fi_NormalMAP"));
                Assert.AreEqual(("IgnAbsCal.m_AirNormXSP", "IgnAbsCal.n_EngNormYSP", "mg/c", "rpm", "°"), bin.AxisSymbols("IgnAbsCal.fi_NormalMAP"));
                CollectionAssert.AreEqual(new[] { 500, 750, 1000, 1250, 1500, 1750, 2000, 2500, 3000, 3500, 4000, 4500, 5000, 5500, 6000, 6500 },
                    bin.GetYaxisValues("IgnAbsCal.fi_NormalMAP"));

                // the gear table's y axis is written in the dictionary ("8 : 0 1 … 7"), it has no x axis
                CollectionAssert.AreEqual(Enumerable.Range(0, 8).ToArray(), bin.GetYaxisValues("TrqLimCal.Trq_ManGear"));
                Assert.IsEmpty(bin.GetXaxisValues("TrqLimCal.Trq_ManGear"));

                // a symbol that only lives in SRAM has no file address and no content to read from the file
                SymbolHelper sram = bin.Symbols.Cast<SymbolHelper>().First(sh => sh.Flash_start_address >= 0x100000 && sh.Length > 0);
                Assert.AreEqual(-1, bin.FileAddress(sram));
                Assert.IsNull(bin.ReadSymbol(sram));
                Assert.AreEqual(0, bin.SymbolAddress(sram.SmartVarname));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [TestMethod]
        public void WritingAMapUpdatesTheChecksum()
        {
            T8Binary bin = OpenCopy(out string dir);
            try
            {
                SymbolHelper map = bin.Find("IgnAbsCal.fi_NormalMAP");
                byte[] data = bin.ReadSymbol(map);
                data[1] ^= 0x01;

                // savedatatobinary alone leaves layer 1 (an MD5 over the code and calibration) stale
                bin.WriteData(bin.FileAddress(map), data);
                Assert.AreEqual(ChecksumResult.Layer1Failed, bin.VerifyChecksum());
                bin.UpdateChecksum();
                Assert.AreEqual(ChecksumResult.Ok, bin.VerifyChecksum());

                data[1] ^= 0x01;
                var log = new TrionicTransactionLog();
                log.OpenTransActionLog(dir, "proj");
                bin.WriteSymbol(bin.FileAddress(map), data, log, "back");
                CollectionAssert.AreEqual(data, bin.ReadSymbol(map));
                Assert.AreEqual(ChecksumResult.Ok, bin.VerifyChecksum());
                Assert.HasCount(1, log.TransCollection);
                Assert.AreEqual("back", log.TransCollection[0].Note);

                // only inside the file, as savedatatobinary
                byte[] before = File.ReadAllBytes(bin.FileName);
                bin.WriteData(0, [1, 2]);
                bin.WriteData(0x100000, [1, 2]);
                CollectionAssert.AreEqual(before, File.ReadAllBytes(bin.FileName));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
