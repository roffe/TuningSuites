using System.Linq;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using T5App.ViewModels;
using T5App.Views;

namespace T5AppTest
{
    public partial class AppTest
    {
        /// <summary>
        /// Settings Ok refreshes the symbol list and the folds stay: a category opened by hand with its subcategories folded, the others
        /// folded. The grid folds again by key alone, so plain subcategory names unfolded the Idle category (Fuel → Idle took its fold).
        /// </summary>
        [TestMethod]
        public void SettingsOkKeepsTheSymbolListFolds()
        {
            string file = CopyOfStockBin("settingsfolds.bin");
            s_session!.Dispatch(async () =>
            {
                var vm = new T5MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                window.CaptureRenderedFrame();
                var grid = window.GetVisualDescendants().OfType<DataGrid>().First(g => g.Name == "SymbolGrid");
                // the shown headers top down: name, + when unfolded
                string[] Headers()
                {
                    window.CaptureRenderedFrame();
                    return [.. grid.GetVisualDescendants().OfType<DataGridRowGroupHeader>().Where(h => h.IsVisible).OrderBy(h => h.Bounds.Y)
                        .Select(h => ((DataGridCollectionViewGroup)h.DataContext!).Key + (h.Classes.Contains(":expanded") ? "+" : ""))];
                }
                int Rows() => grid.GetVisualDescendants().OfType<DataGridRow>().Count(r => r.IsVisible);

                grid.ExpandRowGroup(vm.Symbols!.Groups!.OfType<DataGridCollectionViewGroup>().Single(g => Equals(g.Key, "Fuel")), false);
                string[] before = Headers();
                CollectionAssert.Contains(before, "Fuel+");
                CollectionAssert.Contains(before, "Idle");
                Assert.AreEqual(0, Rows());

                vm.SettingsChanged();
                vm.T5SettingsChanged();
                CollectionAssert.AreEqual(before, Headers());
                Assert.AreEqual(0, Rows());
                Save(window, "symbolfolds");
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
