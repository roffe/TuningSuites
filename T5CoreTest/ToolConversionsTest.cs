using System;
using System.IO;
using System.Linq;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Trionic5Tools;

namespace T5CoreTest
{
    /// <summary>The numbers T5Suite's tools showed: the dyno on a 3.0 bar sensor, the user library's torque, the compare average.</summary>
    [TestClass]
    public class ToolConversionsTest
    {
        private static void InTemp(Action<string> test)
        {
            string dir = Directory.CreateTempSubdirectory("t5tools").FullName;
            try { test(dir); }
            finally { Directory.Delete(dir, true); }
        }

        private static string Copy(string dir, string name)
        {
            string file = Path.Combine(dir, name);
            File.Copy(BinGoldenTest.StockBins().Single(b => Path.GetFileName(b) == name), file);
            return file;
        }

        /// <summary>frmDynoChart scaled Tryck_mat! as a double: raw 188 at 2500 rpm is 225.6 kPa, 1.256 bar and 374 Nm (not 225, 1.25 and 372).</summary>
        [TestMethod]
        public void DynoOnThreeBarSensor() => InTemp(dir =>
        {
            string file = Copy(dir, "4239273.bin");
            using (var fs = new FileStream(file, FileMode.Open))
            {
                // the 3.0 bar marker, as the MAP sensor conversion writes it
                fs.Position = fs.Length - 0x1FF;
                fs.WriteByte(0x01);
            }
            T5Binary bin = T5Binary.Open(file);
            Assert.AreEqual(120, bin.SensorPercent);
            var row = T5InjectionModel.Dyno(bin).Single(d => d.Rpm == 2500);
            Assert.AreEqual(1.256, row.Boost, 1e-9);
            Assert.AreEqual(374, Math.Round(row.Torque));
        });

        /// <summary>frmUserLibrary rounded the torque: 4239273.bin's 284.99999… shows 285.</summary>
        [TestMethod]
        public void UserLibraryTorqueRounded() => InTemp(dir =>
        {
            Assert.AreEqual(285, T5UserLibrary.Scan(Copy(dir, "4239273.bin")).Torque);
        });

        /// <summary>CompareSymbolToCurrentFile's average difference is absolute: Purge_tab! of 4239273 against 4300349 is 5.5, not -5.5.</summary>
        [TestMethod]
        public void CompareAverageAbsolute() => InTemp(dir =>
        {
            var rows = SuiteCompare.Compare(T5Binary.Open(Copy(dir, "4239273.bin")), T5Binary.Open(Copy(dir, "4300349.bin")));
            Assert.AreEqual(5.5, Math.Round(rows.Single(r => r.SymbolName == "Purge_tab!").AverageDifference, 1));
            Assert.AreEqual(85.2, Math.Round(rows.Single(r => r.SymbolName == "Lacc_clear_tab!").AverageDifference, 1));
            Assert.IsTrue(rows.All(r => r.AverageDifference >= 0));
        });
    }
}
