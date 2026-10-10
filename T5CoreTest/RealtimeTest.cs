using System.Collections.Generic;
using System.Linq;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Trionic5Tools;

namespace T5CoreTest
{
    /// <summary>T5Suite's realtime conversions (Trionic5SymbolConverter) on the shared table, with fake SRAM bytes.</summary>
    [TestClass]
    public class RealtimeTest
    {
        [TestMethod]
        public void ConversionsStatusAndCells()
        {
            var bin = T5Binary.Open(BinGoldenTest.StockBins().First(b => b.EndsWith("4239273.BIN")));
            string before = SettingsKey.BaseFolder;
            SettingsKey.BaseFolder = System.IO.Directory.CreateTempSubdirectory("t5settings").FullName;
            try { Run(bin, new AppSettings(new T5SuiteRegistry())); }
            finally
            {
                System.IO.Directory.Delete(SettingsKey.BaseFolder, true);
                SettingsKey.BaseFolder = before;
            }
        }

        // the settings' setters save: only into the test's own folder
        private static void Run(T5Binary bin, AppSettings settings)
        {
            List<RealtimeSymbol> rows = T5Realtime.Rules.Dashboard(bin, settings);
            Assert.IsTrue(rows.Count > 20);
            Assert.AreEqual(2, rows.Single(r => r.Name == "Knock_count_cyl1").Length);

            var bytes = new Dictionary<string, byte[]>
            {
                ["Rpm"] = [0x01, 0x2C],                 // 300 → 3000 rpm
                ["P_medel"] = [150],                    // 1.5 × 0.01 − 1 = 0.5 bar on the stock sensor
                ["Kyl_temp"] = [250],                   // −6 °C
                ["Medeltrot"] = [100],                  // 66
                ["Ign_angle"] = [0xFF, 0xEC],           // −20 → −2.0°
                ["Pgm_status"] = [0x20, 0, 0, 0x02, 0x20, 0], // fuel cut, closed loop, enrichment after fuel cut (bit 37)
                ["AD_sond"] = [25],                     // λ 1.0
                ["Lacc_mangd"] = [1, 2, 3, 4],
            };
            RealtimeSample sample = Realtime.Cycle(rows, r => bytes.GetValueOrDefault(r.Name), System.DateTime.Now, T5Realtime.Rules, everyPass: true);
            Assert.AreEqual(3000, sample["Rpm"]!.Value, 1e-9);
            Assert.AreEqual(0.5, sample["P_medel"]!.Value, 1e-9);
            Assert.AreEqual(-6, sample["Kyl_temp"]!.Value, 1e-9);
            Assert.AreEqual(66, sample["Medeltrot"]!.Value, 1e-9);
            Assert.AreEqual(-2.0, sample["Ign_angle"]!.Value, 1e-9);
            Assert.AreEqual(1.0, sample["AD_sond"]!.Value, 1e-9);
            Assert.AreEqual(3, sample["LoadAccCyl3"]!.Value, 1e-9);
            Assert.AreEqual(0x2002000020L, (long)sample["Pgm_status"]!.Value);
            int status = unchecked((int)(long)sample["Pgm_status"]!.Value);
            Assert.AreEqual("Fuel cut", T5Realtime.Rules.Fuelcut(status));
            Assert.AreEqual("Closed loop", T5Realtime.Rules.Lambda(status));
            Assert.AreEqual("Fuel cut cyl 1, 4", T5Realtime.Rules.Fuelcut(0x8000 | 0x1000));

            // CheckAutoTuneParameters: warm, afterstart and cooling water enrichment done, open loop
            const long ready = 0x10 | 0x4000000 | 0x10000000;
            Assert.IsTrue(T5Autotune.Allowed(ready, settings, out bool idle) && !idle);
            Assert.IsFalse(T5Autotune.Allowed(ready | 0x2000000, settings, out _));     // closed loop
            Assert.IsFalse(T5Autotune.Allowed(ready | 0x2000000000, settings, out _));  // enrichment after fuel cut (byte 4)
            Assert.IsFalse(T5Autotune.Allowed(ready & ~0x10, settings, out _));          // cold
            settings.AllowIdleAutoTune = true;
            Assert.IsTrue(T5Autotune.Allowed(ready | 0x40000000, settings, out idle) && idle);
            Assert.IsTrue(T5Autotune.Enriching(sample, 2));                            // LoadAccCyl3 / 4 are 3 and 4
            Assert.IsFalse(T5Autotune.Enriching(sample, 4));

            // 0.5 bar at 3000 rpm on the main fuel map: the MAP axis column nearest 150, the rpm row nearest 3000
            var tracker = new CellTracker(bin, T5Realtime.Rules.CellRules);
            var cell = tracker.Cell("Insp_mat!", i => i switch { CellInput.Boost => 0.5, CellInput.Rpm => 3000, _ => 0 });
            int[] x = bin.GetXaxisValues("Insp_mat!"), y = bin.GetYaxisValues("Insp_mat!");
            Assert.AreEqual(System.Array.IndexOf(x, x.OrderBy(v => System.Math.Abs(v - 150)).First()), cell!.Value.col);
            Assert.AreEqual(System.Array.IndexOf(y, y.OrderBy(v => System.Math.Abs(v - 3000)).First()), cell.Value.row);
        }
    }
}
