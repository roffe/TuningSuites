using System.IO;
using System.Linq;
using Avalonia.Headless;
using Avalonia.VisualTree;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TxSuite.ViewModels;
using TxSuite.Views;
using SuiteApp.ViewModels;
using T5App.ViewModels;
using T5App.Views;

namespace T5AppTest
{
    public partial class AppTest
    {
        /// <summary>T5Suite's DockAsTab onto the panel of the same symbol: a compare row's maps open as tabs of one window, also against an SRAM snapshot.</summary>
        [TestMethod]
        public void CompareOpensMapsAsTabs()
        {
            string file = CopyOfStockBin("tabs.BIN");
            string other = CopyOfStockBin("tabs-other.BIN", "4300331.bin");
            s_session!.Dispatch(async () =>
            {
                var vm = new MainViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                await vm.CompareToFileAsync(other);
                var results = (CompareResultsViewModel)vm.SelectedViewer!;
                results.Open(results.Rows.SourceCollection.Cast<CompareRow>().Single(r => r.SymbolName == "Insp_mat!"));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var tabs = vm.DockedViewers.OfType<DocumentTabsViewModel>().Single();
                CollectionAssert.AreEqual(new DocumentViewModel[] { results, tabs }, vm.DockedViewers.ToArray());
                // the compare side first and selected, as T7Suite's DockAsTab at index 0
                CollectionAssert.AreEqual(new[] { "Symbol: Insp_mat! [tabs-other.BIN]", "Symbol: Insp_mat! [tabs.BIN]" }, tabs.Items.Select(t => t.Title).ToArray());
                Assert.IsTrue(((MapViewerViewModel)tabs.Items[0]).IsReadOnly);
                Assert.IsFalse(((MapViewerViewModel)tabs.Items[1]).IsReadOnly);
                Assert.AreSame(tabs.Items[0], vm.SelectedViewer);
                Assert.HasCount(2, window.GetVisualDescendants().OfType<Dock.Avalonia.Controls.MdiDocumentWindow>());
                Save(window, "compare-tabs");

                // SRAM <> BIN: the bin's map and the snapshot's
                SymbolHelper sh = vm.Binary!.Find("Tryck_mat!")!;
                byte[] map = vm.Binary.ReadSymbol(sh);
                map[1] ^= 0x10;
                var ram = new byte[0x8000];
                for (int i = 0; i < map.Length; i++) ram[(sh.Start_address + i) % ram.Length] = map[i];
                string ramFile = Path.Combine(s_dir, "tabs.RAM");
                File.WriteAllBytes(ramFile, ram);
                await vm.CompareToSramAsync(ramFile);
                var sram = (CompareResultsViewModel)vm.SelectedViewer!;
                sram.Open(sram.Rows.SourceCollection.Cast<CompareRow>().Single(r => r.SymbolName == "Tryck_mat!"));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var sramTabs = (DocumentTabsViewModel)vm.SelectedViewer!.Tabs!;
                CollectionAssert.AreEqual(new[] { "SRAM Symbol: Tryck_mat! [tabs.RAM]", "Symbol: Tryck_mat! [tabs.BIN]" }, sramTabs.Items.Select(t => t.Title).ToArray());
                Assert.AreEqual("SRAM Symbol: Tryck_mat! [tabs.RAM]", sramTabs.Title);
                Assert.HasCount(4, window.GetVisualDescendants().OfType<Dock.Avalonia.Controls.MdiDocumentWindow>());
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
