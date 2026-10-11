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
        /// The symbol list opens with every category folded; the map in front is selected in it, its category unfolded, at the top of
        /// the list when it wasn't in view. A search
        /// unfolds every category, its end folds them again; other refreshes keep the folds. Its text size comes from Settings.
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
                DataGridRow RowOf(SymbolHelper sh) => grid.GetVisualDescendants().OfType<DataGridRow>().First(r => r.IsVisible && r.DataContext == sh);

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
                    // at the top of the list, not at the bottom edge where ScrollIntoView leaves a row from below
                    Assert.AreEqual(0, RowOf(sh).Bounds.Y, 1, sh.Varname);
                }
                // a symbol already in view stays where it is
                vm.OpenSymbolByName("IgnNormCal.m_AirXSP");
                window.CaptureRenderedFrame();
                Assert.AreEqual("IgnNormCal.m_AirXSP", vm.SelectedSymbol?.Varname);
                Assert.AreEqual(0, RowOf(ign).Bounds.Y, 1);
                vm.SelectedViewer = ignViewer;
                window.CaptureRenderedFrame();
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
                // the text size from Settings, the rows with it; Fluent's 15 px on 32 px rows was too large
                DataGridCell Cell()
                {
                    grid.ScrollIntoView(fuel, null);
                    window.CaptureRenderedFrame();
                    return grid.GetVisualDescendants().OfType<DataGridRow>().First(r => r.IsVisible && r.DataContext == fuel).GetVisualDescendants().OfType<DataGridCell>().First();
                }
                Assert.AreEqual(13, Cell().FontSize);
                try
                {
                    vm.Settings.SymbolListFontSize = 20;
                    vm.SettingsChanged();
                    window.CaptureRenderedFrame();
                    Assert.AreEqual(20, Cell().FontSize);
                    Assert.AreEqual(43, Cell().Bounds.Height, 1);
                }
                finally
                {
                    vm.Settings.SymbolListFontSize = 13;
                    vm.SettingsChanged();
                }
                window.CaptureRenderedFrame();
                Assert.AreEqual(28, Cell().Bounds.Height, 1);

                // one chosen in the list (a key, a click) only scrolls into view
                grid.Focus();
                SymbolHelper below = vm.Binary.Find("LambdaCal.MaxLoadNormTab");
                vm.SelectedSymbol = below;
                window.CaptureRenderedFrame();
                Assert.IsGreaterThan(0, RowOf(below).Bounds.Y);

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
