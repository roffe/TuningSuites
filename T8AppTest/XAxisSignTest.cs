using System.IO;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TxSuite.ViewModels;
using TxSuite.Views;
using SuiteApp.ViewModels;
using T8App.ViewModels;

namespace T8AppTest
{
    public partial class AppTest
    {
        /// <summary>
        /// T8Suite's viewer showed X axis values above 0xF000 negative; its Excel export signed only a high byte of 0xFF and took
        /// the factor of the listed axis, not of the duplicate the viewer reads (this bin has OffsetXSP, not fi_offsetXSP).
        /// </summary>
        [TestMethod]
        public void NegativeXAxisValues()
        {
            string file = Path.Combine(s_dir, "xsign.BIN");
            File.Copy(Path.Combine(Here(), "..", "T8Binaries", "55353231_FA5I_C_FME2_65_FIEF_80d.bin"), file, true);
            s_session!.Dispatch(async () =>
            {
                var vm = new MainViewModel();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                SuiteBinary bin = vm.Binary!;
                double[] X(string map) => MapViewerViewModel.Create(vm, bin, bin.Find(map))!.Map.XAxis!;
                CollectionAssert.AreEqual(new double[] { -200, -100, -50, 0, 30, 100, 200 }, X("AirCtrlCal.Ppart_BoostMap"));
                CollectionAssert.AreEqual(new double[] { -200, -180, -160 }, X("BstKnkCal.MaxAirmass")[..3]);
                Assert.AreEqual(0, X("BstKnkCal.MaxAirmass")[^1]);

                string csv = Path.Combine(s_dir, "xsign.csv");
                SymbolFiles.ExportMapCsv(bin, bin.Find("BstKnkCal.MaxAirmass"), csv);
                Assert.AreEqual(";-20;-18;-16;-14;-12;-10;-9;-8;-7;-6;-5;-4;-3;-2;-1;0", File.ReadAllLines(csv)[1]);
                SymbolFiles.ExportMapCsv(bin, bin.Find("AirCtrlCal.Ppart_BoostMap"), csv);
                Assert.AreEqual(";-200;-100;-50;0;30;100;200", File.ReadAllLines(csv)[1]);
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
