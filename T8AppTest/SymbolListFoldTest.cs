using System;
using System.Linq;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TxSuite.ViewModels;
using TxSuite.Views;

namespace T8AppTest
{
    public partial class AppTest
    {
        /// <summary>
        /// Settings → Show descriptions under symbol names switched on with a category open, then categories folded and unfolded and the
        /// list scrolled at random: the grid lost track of its shown rows (index out of range, an empty list and no menus after) when
        /// its rows grew after it measured them or were taller than its category headers.
        /// </summary>
        [TestMethod]
        public void SymbolListFoldsWithDescriptionLine()
        {
            s_session!.Dispatch(async () =>
            {
                var vm = new MainViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1100, Height = 950 };
                Assert.IsTrue(await vm.OpenFileAsync(CopyOfStockBin("foldlines.bin"), true));
                window.Show();
                window.CaptureRenderedFrame();
                var grid = window.GetVisualDescendants().OfType<DataGrid>().First(g => g.Name == "SymbolGrid");
                grid.ExpandRowGroup(vm.Symbols!.Groups!.OfType<DataGridCollectionViewGroup>().ElementAt(2), false);
                window.CaptureRenderedFrame();
                try
                {
                    vm.Settings.ShowSymbolDescriptionLine = true;
                    vm.SettingsChanged();
                    window.CaptureRenderedFrame();
                    var rnd = new Random(3);
                    for (int i = 0; i < 40; i++)
                    {
                        var groups = vm.Symbols!.Groups!.OfType<DataGridCollectionViewGroup>().ToList();
                        if (rnd.Next(2) == 0) grid.ScrollIntoView(groups[rnd.Next(groups.Count)], null);
                        else if (grid.GetVisualDescendants().OfType<DataGridRowGroupHeader>().Where(h => h.IsVisible).ToList() is { Count: > 0 } headers)
                        {
                            var fold = headers[rnd.Next(headers.Count)].GetVisualDescendants().OfType<ToggleButton>().First();
                            fold.IsChecked = !(fold.IsChecked ?? false);
                        }
                        window.CaptureRenderedFrame();
                    }
                    Assert.IsNotEmpty(grid.GetVisualDescendants().OfType<DataGridRowGroupHeader>().Where(h => h.IsVisible).ToList());
                }
                finally
                {
                    vm.Settings.ShowSymbolDescriptionLine = false;
                    vm.SettingsChanged();
                }
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
