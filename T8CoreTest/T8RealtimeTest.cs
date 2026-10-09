using System;
using System.IO;
using System.Linq;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using T8SuitePro;

namespace T8CoreTest
{
    /// <summary>T8Suite's realtime table on the shared engine code: rows, per-cylinder counters, statuses, the dynamic list's matching.</summary>
    [TestClass]
    public class T8RealtimeTest
    {
        private static string NoSymbolLists => Path.Combine(Path.GetTempPath(), "t8golden-no-symbol-lists");

        private static T8Binary Stock() =>
            T8Binary.Open(BinGoldenTest.StockBins().Single(b => Path.GetFileName(b) == "55353231_FA56_C_FME2_37_FIEF_81c.BIN"), false, NoSymbolLists);

        [TestMethod]
        public void DashboardRowsAndCells()
        {
            T8Binary bin = Stock();
            string dir = Directory.CreateTempSubdirectory("t8rt").FullName;
            string before = SettingsKey.BaseFolder;
            SettingsKey.BaseFolder = dir;
            try
            {
                var rows = T8Realtime.Rules.Dashboard(bin, new AppSettings(new T8SuiteRegistry()));
                Assert.AreEqual("ActualIn.U_Battery", rows[0].Name);
                CollectionAssert.IsSubsetOf(new[] { "Out.X_AccPos", "IgnMastProt.fi_Offset", "AirMassMast.m_Request", "Out.M_EngTrqAct" }, rows.Select(r => r.Name).ToList());
                Assert.IsFalse(rows.Any(r => r.Name == "BFuelProt.CurrentFuelCon"));
                Assert.IsTrue(rows.All(r => r.SramAddress >= 0x100000), "the realtime symbols live in SRAM");

                // the engine's cell in the ignition map from the map's own axes
                int[] air = bin.GetXaxisValues("IgnAbsCal.fi_NormalMAP"), rpm = bin.GetYaxisValues("IgnAbsCal.fi_NormalMAP");
                var tracker = new CellTracker(bin, T8Realtime.Rules.CellRules);
                Assert.AreEqual((5, 3), tracker.Cell("IgnAbsCal.fi_NormalMAP", i => i == CellInput.Rpm ? rpm[3] + 1 : air[5]));
                Assert.IsNull(tracker.Cell("Not.AMap", _ => 0));
            }
            finally
            {
                SettingsKey.BaseFolder = before;
                Directory.Delete(dir, true);
            }
        }

        [TestMethod]
        public void PassesAndStatuses()
        {
            var knock = new RealtimeSymbol { Name = "KnkDet.KnockCyl", SramAddress = 0x100100, Length = 4 };
            var counts = new RealtimeSymbol { Name = "KnkDetAdap.KnkCntCyl", SramAddress = 0x100200, Length = 8 };
            var offset = new RealtimeSymbol { Name = "IgnMastProt.fi_Offset", SramAddress = 0x100300, Length = 2, Correction = 0.1 };
            byte[] Read(RealtimeSymbol r) => r == knock ? [1, 2, 3, 4] : r == counts ? [0, 10, 0, 20, 0, 30, 1, 0] : [0xFF, 0xF6];
            RealtimeSample s = Realtime.Cycle([knock, counts, offset], Read, DateTime.Now, T8Realtime.Rules);
            Assert.AreEqual(3, s["KnockCyl3"]);
            Assert.AreEqual(256, s["KnkCntCyl4"]);
            Assert.AreEqual(-1, s["IgnMastProt.fi_Offset"]!.Value, 1e-9);

            Assert.AreEqual("Closed loop not activated", T8Realtime.Rules.Lambda(1));
            Assert.AreEqual("Starter control relay circuit short to ground", T8Realtime.Rules.Fuelcut(8));
            Assert.AreEqual("Crankcase vent error", T8Realtime.Rules.AirDemand(54));
            Assert.AreEqual("99", T8Realtime.Rules.AirDemand(99));
            Assert.AreEqual("bin-20261010-CanTraceExt.t8l", Path.GetFileName(RealtimeLog.FileName("/x/bin.BIN", new DateTime(2026, 10, 10), T8Realtime.Rules.LogExtension)));
        }

        /// <summary>T8Suite matched the dynamic list's data by counting the table's rows, so a row that wasn't in the list shifted every later one.</summary>
        [TestMethod]
        public void DynamicListDataGoesToItsRows()
        {
            var rpm = new RealtimeSymbol { Name = "ActualIn.n_Engine", SramAddress = 0x100010, Length = 2 };
            var big = new RealtimeSymbol { Name = "Some.Table", SramAddress = 0x100020, Length = 20 };
            var flash = new RealtimeSymbol { Name = "Flash.Only", Length = 2 };
            var cut = new RealtimeSymbol { Name = "FCut.CutStatus", SramAddress = 0x100030, Length = 1 };
            var derived = new RealtimeSymbol { Name = "KnockCyl1", Derived = true };
            RealtimeSymbol[] table = [rpm, big, flash, cut, derived];
            RealtimeSymbol[] list = table.Where(T8RealtimeEngine.InList).ToArray();
            CollectionAssert.AreEqual(new[] { rpm, cut }, list);
            var data = T8RealtimeEngine.Split(list, [0x03, 0x20, 0x06]);
            CollectionAssert.AreEqual(new byte[] { 0x03, 0x20 }, data[rpm]);
            CollectionAssert.AreEqual(new byte[] { 0x06 }, data[cut]);
            Assert.IsFalse(data.ContainsKey(big));
        }
    }
}
