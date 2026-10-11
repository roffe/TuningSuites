using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SuiteApp.ViewModels;
using T7App.ViewModels;
using T7App.Views;

namespace T7AppTest
{
    public partial class AppTest
    {
        /// <summary>
        /// The symbol list opens with every category folded; the map in front is selected in it, its category unfolded. A search
        /// unfolds every category, its end folds them again; other refreshes keep the folds.
        /// </summary>
        [TestMethod]
        public void SymbolListFollowsMapInFront()
        {
            s_session!.Dispatch(async () =>
            {
                var vm = new T7MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                // opened before the list is laid out, as Auto load last file on startup does
                Assert.IsTrue(await vm.OpenFileAsync(Path.Combine(s_dir, "5168646.bin"), true));
                window.Show();
                window.CaptureRenderedFrame();
                var grid = window.GetVisualDescendants().OfType<DataGrid>().First(g => g.Name == "SymbolGrid");
                SymbolHelper[] Shown() => [.. grid.GetVisualDescendants().OfType<DataGridRow>().Where(r => r.IsVisible).Select(r => r.DataContext).OfType<SymbolHelper>()];
                Assert.IsEmpty(Shown(), "folded on open");

                SymbolHelper ign = vm.Binary!.Find("IgnNormCal.Map"), fuel = vm.Binary.Find("BFuelCal.Map");
                Assert.AreNotEqual(ign.Category, fuel.Category);
                var ignViewer = vm.OpenSymbolByName("IgnNormCal.Map")!;
                var fuelViewer = vm.OpenSymbolByName("BFuelCal.Map")!;
                foreach (var (viewer, sh) in new[] { (ignViewer, ign), (fuelViewer, fuel), (ignViewer, ign) })
                {
                    vm.SelectedViewer = viewer;
                    window.CaptureRenderedFrame();
                    Assert.AreSame(sh, vm.SelectedSymbol);
                    Assert.AreSame(sh, grid.SelectedItem);
                    CollectionAssert.Contains(Shown(), sh, sh.Varname);
                }
                // a search shows its matches unfolded; cleared, folded again but the selected symbol's category
                vm.SearchText = "Cal.Map";
                window.CaptureRenderedFrame();
                Assert.IsTrue(grid.GetVisualDescendants().OfType<DataGridRowGroupHeader>().Where(h => h.IsVisible).All(h => h.Classes.Contains(":expanded")));
                Assert.IsGreaterThan(2, Shown().Select(s => s.Category).Distinct().Count());
                vm.SearchText = "";
                window.CaptureRenderedFrame();
                Assert.IsTrue(Shown().All(s => s.Category == vm.SelectedSymbol?.Category));
                vm.SelectedViewer = fuelViewer;
                window.CaptureRenderedFrame();
                Assert.IsTrue(Shown().Contains(fuel) && Shown().All(s => s.Category == fuel.Category));
                // other refreshes keep the folds
                vm.SettingsChanged();
                window.CaptureRenderedFrame();
                Assert.IsTrue(Shown().Contains(fuel) && Shown().All(s => s.Category == fuel.Category));
                // a file opened into the shown list: folded too, nothing selected (the grid made the first row current, unfolding its category)
                Assert.IsTrue(await vm.OpenFileAsync(Path.Combine(s_dir, "5168646.bin"), true));
                window.CaptureRenderedFrame();
                Assert.IsEmpty(Shown(), "folded on a second open");
                Assert.IsNull(vm.SelectedSymbol);
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
