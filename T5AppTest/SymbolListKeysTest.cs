using System.Linq;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
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
        /// Left in the symbol list folds the nearest category above, as in a tree: on a symbol its own (Insp_mat! in Fuel → Basic), on
        /// that folded subcategory its parent, the keyboard then on the folded category. On a symbol it only moved a column left.
        /// </summary>
        [TestMethod]
        public void LeftFoldsTheCategoryAbove()
        {
            string file = CopyOfStockBin("leftfold.bin");
            s_session!.Dispatch(async () =>
            {
                var vm = new MainViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                window.CaptureRenderedFrame();
                var grid = window.GetVisualDescendants().OfType<DataGrid>().First(g => g.Name == "SymbolGrid");
                vm.SelectedSymbol = vm.Binary!.Find("Insp_mat!");
                window.CaptureRenderedFrame();
                grid.Focus();
                DataGridRowGroupHeader[] Headers()
                {
                    window.CaptureRenderedFrame();
                    return [.. grid.GetVisualDescendants().OfType<DataGridRowGroupHeader>().Where(h => h.IsVisible)];
                }
                static string Name(DataGridRowGroupHeader h) => ((DataGridCollectionViewGroup)h.DataContext!).Key?.ToString() ?? "";
                string Current() => Name(Headers().Single(h => h.Classes.Contains(":current")));
                bool Unfolded(string key) => Headers().Any(h => Name(h) == key && h.Classes.Contains(":expanded"));
                Assert.IsTrue(Unfolded("Fuel") && Unfolded("Basic"));

                window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
                Assert.AreEqual("Basic", Current());
                Assert.IsFalse(Unfolded("Basic"));
                Assert.IsTrue(Unfolded("Fuel"));

                window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft, null);
                Assert.AreEqual("Fuel", Current());
                Assert.IsFalse(Unfolded("Fuel"));
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
