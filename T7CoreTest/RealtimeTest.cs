using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using T7;

namespace T7CoreTest
{
    [TestClass]
    public class RealtimeTest
    {
        private static string Here([CallerFilePath] string path = "") => Path.GetDirectoryName(path);

        private static T7Binary Bin() => T7Binary.Open(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), 0, false);

        [TestMethod]
        public void DecodesBigEndianAndSignedByName()
        {
            Assert.AreEqual(0x1234, Realtime.Decode("ActualIn.n_Engine", [0x12, 0x34]));
            Assert.AreEqual(-5, Realtime.Decode("ActualIn.T_Engine", [0xFF, 0xFB]));
            Assert.AreEqual(65531, Realtime.Decode("ActualIn.n_Engine", [0xFF, 0xFB]));
            Assert.AreEqual(0x01020304u, Realtime.Decode("x", [1, 2, 3, 4]));
            Assert.AreEqual(0, Realtime.Decode("x", [1, 2, 3]));
        }

        [TestMethod]
        public void CycleReadsByDelayScalesAndKeepsValuesOnFailure()
        {
            var boost = new RealtimeSymbol { Name = "In.p_AirInlet", SramAddress = 0xF00000, Length = 2, Offset = -1, Correction = 0.001 };
            var temp = new RealtimeSymbol { Name = "ActualIn.T_Engine", SramAddress = 0xF00010, Length = 2, Reload = 3 };
            var knock = new RealtimeSymbol { Name = "KnkDetAdap.KnkCntCyl", SymbolNumber = 7, Length = 8 };
            RealtimeSymbol[] rows = [boost, temp, knock];
            var reads = new List<string>();
            bool fail = false;
            byte[] Read(RealtimeSymbol r)
            {
                reads.Add(r.Name);
                if (fail) return null;
                return r.Length == 8 ? [0, 1, 0, 2, 0, 3, 0, 4] : r.Name == "In.p_AirInlet" ? [0x05, 0xDC] : [0, 90];
            }

            RealtimeSample s = Realtime.Cycle(rows, Read, DateTime.Now);
            Assert.AreEqual(0.5, s["In.p_AirInlet"]!.Value, 1e-9);   // 1500 mbar abs → 0.5 bar
            Assert.AreEqual(90, s["ActualIn.T_Engine"]);
            CollectionAssert.AreEqual(new double[] { 1, 2, 3, 4 }, new[] { "KnockCyl1", "KnockCyl2", "KnockCyl3", "KnockCyl4" }.Select(n => s[n]!.Value).ToArray());

            // the temperature waits two passes; a failed read keeps the last value
            reads.Clear();
            fail = true;
            s = Realtime.Cycle(rows, Read, DateTime.Now);
            CollectionAssert.DoesNotContain(reads, "ActualIn.T_Engine");
            Assert.AreEqual(0.5, s["In.p_AirInlet"]!.Value, 1e-9);
            Assert.AreEqual(90, s["ActualIn.T_Engine"]);
            Realtime.Cycle(rows, Read, DateTime.Now);
            reads.Clear();
            Realtime.Cycle(rows, Read, DateTime.Now);
            CollectionAssert.Contains(reads, "ActualIn.T_Engine");
        }

        [TestMethod]
        public void DashboardLayoutsAndTracking()
        {
            T7Binary bin = Bin();
            string dir = Directory.CreateTempSubdirectory("t7rt").FullName;
            string before = SettingsKey.BaseFolder;
            SettingsKey.BaseFolder = dir;
            try
            {
                List<RealtimeSymbol> dash = Realtime.Dashboard(bin, new AppSettings(new T7SuiteRegistry()));
                RealtimeSymbol rpm = dash.Single(r => r.Name == "ActualIn.n_Engine");
                Assert.AreEqual(bin.FindAny("ActualIn.n_Engine")!.Start_address, rpm.SramAddress);
                Assert.AreEqual(5, dash.Single(r => r.Name == "ActualIn.T_Engine").Reload);
                Assert.IsTrue(dash.Any(r => r.Name == "Lambda.LambdaInt"));

                // a layout written by T7Suite on a Swedish Windows (comma decimals, 9 fields) and one of ours
                string old = Path.Combine(dir, "old.t7rtl");
                File.WriteAllText(old, "Out.X_AccPedal|10|0|100|0|0,1|10|1|2\r\n");
                RealtimeSymbol tps = Realtime.LoadLayout(old, bin).Single();
                Assert.AreEqual(0.1, tps.Correction, 1e-9);
                Assert.AreEqual(bin.FindAny("Out.X_AccPedal")!.Start_address, tps.SramAddress);   // the bin's, not the saved 1

                var user = Realtime.FromSymbol(bin.FindAny("BFuelProt.t_InjActual") ?? bin.FindAny("Out.X_AccPedal")!);
                user.Description = "mine";
                string ours = Path.Combine(dir, "ours.t7rtl");
                Realtime.SaveLayout(ours, [rpm, user]);
                RealtimeSymbol back = Realtime.LoadLayout(ours, bin).Single();
                Assert.AreEqual(user.Name, back.Name);
                Assert.AreEqual("mine", back.Description);
                Assert.AreEqual(user.Correction, back.Correction);

                Assert.HasCount(dash.Count + 1, Realtime.Merge(dash, [back, tps]));
            }
            finally
            {
                SettingsKey.BaseFolder = before;
                Directory.Delete(dir, true);
            }

            // the ignition map cell at the axes' own breakpoints
            var tracker = new CellTracker(bin);
            int[] air = bin.GetXaxisValues("IgnNormCal.Map"), rpms = bin.GetYaxisValues("IgnNormCal.Map");
            var cell = tracker.Cell("IgnNormCal.Map", i => i == CellTracker.Input.Rpm ? rpms[3] + 1 : air[5]);
            Assert.AreEqual((5, 3), cell);
            Assert.IsNull(tracker.Cell("BoostCal.PMap", _ => 0));
        }

        [TestMethod]
        public void LogLinesRoundTripInAnyCulture()
        {
            var time = new DateTime(2026, 10, 9, 14, 3, 22, 123);
            string line = T7Log.Line(time, [("ActualIn.n_Engine", 850), ("In.p_AirInlet", 0.1 * 3)], true);
            Assert.AreEqual("09/10/2026 14:03:22.123|ActualIn.n_Engine=850|In.p_AirInlet=0.3|IMPORTANTLINE=1|", line);
            Assert.IsTrue(T7Log.TryParse(line, out DateTime t, out var values));
            Assert.AreEqual(time, t);
            Assert.AreEqual(0.3, values[1].Value, 1e-12);
            Assert.AreEqual(("IMPORTANTLINE", 1d), values[2]);

            // T7Suite on a Swedish Windows: dots in the date, comma decimals
            Assert.IsTrue(T7Log.TryParse("09.10.2026 14.03.22.123|In.p_AirInlet=0,75|", out t, out values));
            Assert.AreEqual(time, t);
            Assert.AreEqual(0.75, values[0].Value, 1e-12);
            Assert.IsFalse(T7Log.TryParse("garbage", out _, out _));

            Assert.AreEqual("Closed loop activated", RealtimeStatus.Lambda(0));
            Assert.AreEqual("Airmass limit (pressure guard)", RealtimeStatus.Fuelcut(6));
            Assert.AreEqual("99", RealtimeStatus.AirDemand(99));
        }
    }
}
