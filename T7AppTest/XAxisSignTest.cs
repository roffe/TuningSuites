using System.IO;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SuiteApp.ViewModels;
using T7App.ViewModels;

namespace T7AppTest
{
    public partial class AppTest
    {
        /// <summary>
        /// T7Suite's viewer showed X axis values above 0xF000 negative (headers, 3D labels, slider caption all read the map's X
        /// axis); its Excel export signed only a high byte of 0xFF.
        /// </summary>
        [TestMethod]
        public void NegativeXAxisValues()
        {
            string file = Path.Combine(s_dir, "xsign.bin");
            File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), file, true);
            s_session!.Dispatch(async () =>
            {
                var vm = new T7MainWindowViewModel();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                SuiteBinary bin = vm.Binary!;
                double[] X(string map) => MapViewerViewModel.Create(vm, bin, bin.Find(map))!.Map.XAxis!;
                CollectionAssert.AreEqual(new double[] { -200, -100, -50, 0, 30, 100, 200 }, X("BoostCal.PMap"));
                CollectionAssert.AreEqual(new double[] { -40, -20, 0, 20 }, X("TorqueCal.m_AirTorqMap")[..4]);
                // MaxAirmass comes signed (and scaled) from the file already
                Assert.AreEqual(-20, X("BstKnkCal.MaxAirmass")[0]);

                string csv = Path.Combine(s_dir, "xsign.csv");
                SymbolFiles.ExportMapCsv(bin, bin.Find("TorqueCal.m_AirTorqMap"), csv);
                StringAssert.StartsWith(File.ReadAllLines(csv)[1], ";-40;-20;0;20;");
                SymbolFiles.ExportMapCsv(bin, bin.Find("BoostCal.PMap"), csv);
                Assert.AreEqual(";-200;-100;-50;0;30;100;200", File.ReadAllLines(csv)[1]);
                // 0xF000..0xFEFF: the viewer signs it (T7Suite's viewer), the CSV keeps it as it is (the sheet's ConvertSignedValue)
                CollectionAssert.AreEqual(new double[] { -500, -300, -250 }, X("TransCal.DecFacMap")[..3]);
                SymbolFiles.ExportMapCsv(bin, bin.Find("TransCal.DecFacMap"), csv);
                StringAssert.StartsWith(File.ReadAllLines(csv)[1], ";65036;65236;-250;");
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
