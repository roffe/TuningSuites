using System.IO;
using System.Linq;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Trionic5Tools;

namespace T5CoreTest
{
    [TestClass]
    public class MapCsvAxisTest
    {
        /// <summary>T5Suite's Excel export wrote the X axis with its axis symbol's factor and offset: MAP axes in bar.</summary>
        [TestMethod]
        public void XAxisInItsUnits()
        {
            string dir = Directory.CreateTempSubdirectory("t5csv").FullName;
            try
            {
                string file = Path.Combine(dir, "4239273.BIN");
                File.Copy(BinGoldenTest.StockBins().Single(b => Path.GetFileName(b) == "4239273.bin"), file);
                T5Binary bin = T5Binary.Open(file);
                string XRow(string map)
                {
                    string csv = Path.Combine(dir, "map.csv");
                    SymbolFiles.ExportMapCsv(bin, bin.Find(map), csv);
                    return File.ReadAllLines(csv)[1];
                }
                StringAssert.StartsWith(XRow("Ign_map_0!"), ";-1;-0.84;-0.76;");
                StringAssert.StartsWith(XRow("Insp_mat!"), ";-0.78;-0.7;-0.62;");
                Assert.AreEqual(";0.05;0.15;0.3;0.5", XRow("P_fors!"));
                // factor 1, offset 0: as before
                Assert.AreEqual(XRow("Tryck_mat!"), ";" + string.Join(';', bin.GetXaxisValues("Tryck_mat!")));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
