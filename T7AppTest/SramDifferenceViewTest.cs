using System.IO;
using System.Linq;
using CommonSuite;
using MapControls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SuiteApp.ViewModels;
using T7App.ViewModels;
using T7App.Views;

namespace T7AppTest
{
    public partial class AppTest
    {
        /// <summary>T7Suite opened the SRAM snapshots' symbol difference in Easy, whatever the default view.</summary>
        [TestMethod]
        public void SramSnapshotDifferenceOpensInEasy()
        {
            string file = Path.Combine(s_dir, "sramdiff.bin");
            File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), file, true);
            s_session!.Dispatch(async () =>
            {
                var vm = new T7MainWindowViewModel();
                SuiteViewType before = vm.Settings.DefaultViewType;
                vm.Settings.DefaultViewType = SuiteViewType.Decimal;
                try
                {
                    var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                    window.Show();
                    Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                    SymbolHelper sh = vm.Binary!.Find("IgnNormCal.Map")!;
                    var ram = new byte[0x10000];
                    string ram1 = Path.Combine(s_dir, "sramdiff1.RAM"), ram2 = Path.Combine(s_dir, "sramdiff2.RAM");
                    File.WriteAllBytes(ram1, ram);
                    ram[sh.Start_address & 0xFFFF]++;
                    File.WriteAllBytes(ram2, ram);
                    await vm.CompareSramAsync(ram1, ram2);
                    var results = (CompareResultsViewModel)vm.SelectedViewer!;
                    results.ShowDifferenceMap(results.Rows.SourceCollection.Cast<CompareRow>().Single(r => r.SymbolName == "IgnNormCal.Map"));
                    var diff = (MapViewerViewModel)vm.SelectedViewer!;
                    Assert.AreEqual("SRAM symbol difference: IgnNormCal.Map [sramdiff1.RAM vs sramdiff2.RAM]", diff.Title);
                    Assert.AreEqual(MapViewType.Easy, diff.ViewType);
                    window.Close();
                }
                finally
                {
                    vm.Settings.DefaultViewType = before;
                }
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
