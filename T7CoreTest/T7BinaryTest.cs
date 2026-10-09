using System.IO;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using T7;

namespace T7CoreTest
{
    [TestClass]
    public class T7BinaryTest
    {
        private static string Here([CallerFilePath] string path = "") => Path.GetDirectoryName(path);

        private static string m_dir;
        private static T7Binary m_bin;

        // a copy: opening writes <bin>.xml next to the file
        [ClassInitialize]
        public static void Open(TestContext _)
        {
            m_dir = Directory.CreateTempSubdirectory("t7bin").FullName;
            string file = Path.Combine(m_dir, "5168646.bin");
            File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), file);
            Assert.IsTrue(T7Binary.IsValidFile(file));
            m_bin = T7Binary.Open(file, 0, false);
        }

        [ClassCleanup]
        public static void Cleanup() => Directory.Delete(m_dir, true);

        [TestMethod]
        public void HeaderAndFlags()
        {
            Assert.AreEqual("EA1WF0LC.47D", m_bin.SoftwareVersion.Trim());
            Assert.IsFalse(m_bin.IsSoftwareOpen);
            Assert.IsGreaterThan(3000, m_bin.Symbols.Count);
        }

        [TestMethod]
        public void IgnitionMap()
        {
            const string map = "IgnNormCal.Map";
            Assert.AreEqual(18, m_bin.TableWidth(map));
            Assert.IsTrue(m_bin.IsSixteenBitTable(map));
            Assert.AreEqual(0.1, m_bin.GetMapCorrectionFactor(map), 1e-12);
            Assert.HasCount(576, m_bin.ReadSymbol(m_bin.Find(map)));
            int[] x = m_bin.GetXaxisValues(map), y = m_bin.GetYaxisValues(map);
            Assert.HasCount(18, x);
            Assert.HasCount(16, y);
            Assert.AreEqual(500, y[0]);   // rpm
            Assert.IsLessThan(x[17], x[0]); // mg/c rising
            var (_, _, xd, yd, _) = T7Binary.AxisSymbols(map);
            Assert.AreEqual("mg/c", xd);
            Assert.AreEqual("rpm", yd);
            Assert.HasCount(32, m_bin.OpenLoopTable(map));
        }

        [TestMethod]
        public void FuelMapIsEightBit()
        {
            Assert.AreEqual(18, m_bin.TableWidth("BFuelCal.Map"));
            Assert.IsFalse(m_bin.IsSixteenBitTable("BFuelCal.Map"));
            CollectionAssert.AreEqual(new[] { -1, 1, 2, 3, 4, 5 }, m_bin.GetYaxisValues("TorqueCal.M_ManGearLim"));
        }

        [TestMethod]
        public void CorrectionFactorParsing()
        {
            Assert.AreEqual(1, T7Binary.CorrectionFactor("No.Such.Symbol", 0));
            // as in frmMain the hard-coded fallbacks only apply when a "Resolution is" text parses to 0; this help text has none
            Assert.AreEqual(1, T7Binary.CorrectionFactor("MAFCal.cd_ThrottleMap", 0));
        }
    }
}
