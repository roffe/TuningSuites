using System.Linq;
using Avalonia.Headless;
using Avalonia.VisualTree;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SuiteApp.ViewModels;
using T8App.ViewModels;
using T8App.Views;
using T8SuitePro;

namespace T8AppTest
{
    public partial class AppTest
    {
        /// <summary>T8Suite's DockAsTab onto the panel of the same symbol: a compare row's maps open as tabs of one window.</summary>
        [TestMethod]
        public void CompareOpensMapsAsTabs()
        {
            string file = CopyOfStockBin("tabs.BIN"), other = CopyOfStockBin("tabs-other.BIN");
            T8Binary copy = T8Binary.Open(other, false);
            SymbolHelper ign = copy.Find("IgnAbsCal.fi_NormalMAP");
            byte[] data = copy.ReadSymbol(ign);
            data[1] ^= 1;
            copy.WriteSymbol(copy.FileAddress(ign), data);
            s_session!.Dispatch(async () =>
            {
                var vm = new T8MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                await vm.CompareToFileAsync(other);
                var results = (CompareResultsViewModel)vm.SelectedViewer!;
                results.Open(results.Rows.Cast<CompareRow>().Single());
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var tabs = vm.DockedViewers.OfType<DocumentTabsViewModel>().Single();
                CollectionAssert.AreEqual(new DocumentViewModel[] { results, tabs }, vm.DockedViewers.ToArray());
                // the compare side first and selected, as T7Suite's DockAsTab at index 0
                CollectionAssert.AreEqual(new[] { "Symbol: IgnAbsCal.fi_NormalMAP [tabs-other.BIN]", "Symbol: IgnAbsCal.fi_NormalMAP [tabs.BIN]" },
                    tabs.Items.Select(t => t.Title).ToArray());
                Assert.IsTrue(((MapViewerViewModel)tabs.Items[0]).IsReadOnly);
                Assert.IsFalse(((MapViewerViewModel)tabs.Items[1]).IsReadOnly);
                Assert.AreSame(tabs.Items[0], vm.SelectedViewer);
                Assert.HasCount(2, window.GetVisualDescendants().OfType<Dock.Avalonia.Controls.MdiDocumentWindow>());
                Save(window, "compare-tabs");
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
