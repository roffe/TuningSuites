using System.Linq;
using Avalonia.Headless;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TxSuite.ViewModels;
using TxSuite.Views;
using T5App.ViewModels;
using T5App.Views;

namespace T5AppTest
{
    public partial class AppTest
    {
        /// <summary>T8Suite's map preview popup in T5 too (the symbol list's hover is shared, T7AppTest checks it).</summary>
        [TestMethod]
        public void MapPreviewPopup()
        {
            string file = CopyOfStockBin("preview.BIN");
            s_session!.Dispatch(async () =>
            {
                var vm = new MainViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                SymbolHelper map = vm.Binary!.Find("Insp_mat!");
                Assert.IsTrue(vm.Settings.ShowMapPreviewPopup);
                try
                {
                    vm.Settings.ShowMapPreviewPopup = false;
                    Assert.IsNull(vm.MapPreview(map));
                    vm.Settings.ShowMapPreviewPopup = true;
                    Assert.AreEqual(16, vm.MapPreview(map)!.Map.Cols);
                    // none for a symbol that only lives in SRAM
                    Assert.IsNull(vm.MapPreview(vm.Binary.Symbols.Cast<SymbolHelper>().First(s => s.Flash_start_address == 0 && s.Length > 1)));
                }
                finally
                {
                    vm.Settings.ShowMapPreviewPopup = true;
                }
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
