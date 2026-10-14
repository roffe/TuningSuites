using System.Linq;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TxSuite.ViewModels;
using TxSuite.Views;
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
                var vm = new MainViewModel();
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
                vm.T5.SettingsChanged();
                CollectionAssert.AreEqual(before, Headers());
                Assert.AreEqual(0, Rows());
                Save(window, "symbolfolds");
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Settings Ok keeps a clicked category or subcategory current, as it keeps a selected symbol; the refresh made the top one current.
        /// </summary>
        [TestMethod]
        public void SettingsOkKeepsTheCurrentCategory()
        {
            string file = CopyOfStockBin("settingscurrent.bin");
            s_session!.Dispatch(async () =>
            {
                var vm = new MainViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                window.CaptureRenderedFrame();
                var grid = window.GetVisualDescendants().OfType<DataGrid>().First(g => g.Name == "SymbolGrid");
                DataGridRowGroupHeader Header(object key)
                {
                    window.CaptureRenderedFrame();
                    return grid.GetVisualDescendants().OfType<DataGridRowGroupHeader>()
                        .Single(h => h.IsVisible && Equals(((DataGridCollectionViewGroup)h.DataContext!).Key, key));
                }
                object? Current()
                {
                    window.CaptureRenderedFrame();
                    return grid.GetVisualDescendants().OfType<DataGridRowGroupHeader>()
                        .SingleOrDefault(h => h.IsVisible && h.Classes.Contains(":current"))?.DataContext is DataGridCollectionViewGroup g ? g.Key : null;
                }
                // Down from header to header until the key's is current, as a user walks the list
                void Walk(object key)
                {
                    grid.Focus();
                    for (int i = 0; i < 30 && !Equals(Current(), key); i++)
                        window.KeyPress(Avalonia.Input.Key.Down, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.ArrowDown, null);
                }

                Walk("Idle");
                Assert.AreEqual("Idle", Current());
                vm.SettingsChanged();
                Assert.AreEqual("Idle", Current());
                Assert.IsFalse(Header("Idle").Classes.Contains(":expanded"));

                // a subcategory, under an unfolded category: Fuel → Idle, not the Idle category
                grid.ExpandRowGroup(vm.Symbols!.Groups!.OfType<DataGridCollectionViewGroup>().Single(g => Equals(g.Key, "Fuel")), false);
                var fuelIdle = new CommonSuite.GroupKey("Fuel", "Idle");
                Walk(fuelIdle);
                Assert.AreEqual(fuelIdle, Current());
                vm.SettingsChanged();
                Assert.AreEqual(fuelIdle, Current());
                Assert.IsTrue(Header("Fuel").Classes.Contains(":expanded"));
                Assert.IsFalse(Header(fuelIdle).Classes.Contains(":expanded"));
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
