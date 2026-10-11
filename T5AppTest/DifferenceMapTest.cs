using System.IO;
using System.Linq;
using CommonSuite;
using MapControls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SuiteApp.ViewModels;
using T5App.ViewModels;
using T5App.Views;

namespace T5AppTest
{
    public partial class AppTest
    {
        /// <summary>T5Suite's difference viewers: |a-b| × factor without the offset, the compare file's axes, SRAM &lt;&gt; BIN in Decimal.</summary>
        [TestMethod]
        public void DifferenceMaps()
        {
            string file = CopyOfStockBin("diff.BIN");
            string other = CopyOfStockBin("diff-other.BIN", "4300331.bin");
            s_session!.Dispatch(async () =>
            {
                var vm = new T5MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                await vm.CompareToFileAsync(other);
                var results = (CompareResultsViewModel)vm.SelectedViewer!;
                var rows = results.Rows.SourceCollection.Cast<CompareRow>().ToList();

                // Tryck_mat! (× 0.01, offset -1): an unchanged cell reads 0.00, not -1.00
                results.ShowDifferenceMap(rows.Single(r => r.SymbolName == "Tryck_mat!"));
                var tryck = (MapViewerViewModel)vm.SelectedViewer!;
                Assert.AreEqual("Symbol difference: Tryck_mat! [diff-other.BIN]", tryck.Title);
                Assert.IsTrue(Enumerable.Range(0, tryck.Map.Count).Any(i => tryck.Map[i] != 0));
                for (int i = 0; i < tryck.Map.Count; i++) Assert.AreEqual(tryck.Map[i] * 0.01, tryck.Map.Physical(i), 1e-9);

                // Insp_mat!: the axes of the compare file (its Fuel_map_xaxis! / yaxis! differ from the open file's)
                CompareRow insp = rows.Single(r => r.SymbolName == "Insp_mat!");
                results.Open(insp);
                var mine = vm.Viewers.OfType<MapViewerViewModel>().Single(v => v.Title == "Symbol: Insp_mat! [diff.BIN]");
                var theirs = vm.Viewers.OfType<MapViewerViewModel>().Single(v => v.Title == "Symbol: Insp_mat! [diff-other.BIN]");
                results.ShowDifferenceMap(insp);
                var diff = (MapViewerViewModel)vm.SelectedViewer!;
                Assert.AreEqual("Symbol difference: Insp_mat! [diff-other.BIN]", diff.Title);
                CollectionAssert.AreEqual(theirs.Map.XAxis, diff.Map.XAxis);
                CollectionAssert.AreEqual(theirs.Map.YAxis, diff.Map.YAxis);
                CollectionAssert.AreNotEqual(mine.Map.XAxis, diff.Map.XAxis);
                CollectionAssert.AreNotEqual(mine.Map.YAxis, diff.Map.YAxis);
                Assert.AreEqual(0, Enumerable.Range(0, diff.Map.Count).Min(diff.Map.Physical));

                // SRAM <> BIN: a snapshot with the bin's Tryck_mat!, one value 50 higher; opens in Decimal on the raw difference
                SymbolHelper sh = vm.Binary!.Find("Tryck_mat!")!;
                byte[] map = vm.Binary.ReadSymbol(sh);
                map[1] = (byte)(map[1] < 128 ? map[1] + 50 : map[1] - 50);
                var ram = new byte[0x8000];
                for (int i = 0; i < map.Length; i++) ram[(sh.Start_address + i) % ram.Length] = map[i];
                string ramFile = Path.Combine(s_dir, "diff.RAM");
                File.WriteAllBytes(ramFile, ram);
                await vm.CompareToSramAsync(ramFile);
                var sram = (CompareResultsViewModel)vm.SelectedViewer!;
                sram.ShowDifferenceMap(sram.Rows.SourceCollection.Cast<CompareRow>().Single(r => r.SymbolName == "Tryck_mat!"));
                var sramDiff = (MapViewerViewModel)vm.SelectedViewer!;
                Assert.AreEqual("SRAM symbol difference: Tryck_mat! [diff.RAM]", sramDiff.Title);
                Assert.AreEqual(MapViewType.Decimal, sramDiff.ViewType);
                Assert.AreEqual(50, sramDiff.Map[1]);
                Assert.AreEqual(0.5, sramDiff.Map.Physical(1), 1e-9);
                Assert.AreEqual(0, sramDiff.Map.Physical(0));

                // two snapshots: the viewer's usual view
                string ramFile2 = Path.Combine(s_dir, "diff2.RAM");
                ram[sh.Start_address % ram.Length] ^= 1;
                File.WriteAllBytes(ramFile2, ram);
                await vm.CompareSramAsync(ramFile, ramFile2);
                var snapshots = (CompareResultsViewModel)vm.SelectedViewer!;
                snapshots.ShowDifferenceMap(snapshots.Rows.SourceCollection.Cast<CompareRow>().Single(r => r.SymbolName == "Tryck_mat!"));
                var snapshotDiff = (MapViewerViewModel)vm.SelectedViewer!;
                Assert.AreEqual(tryck.ViewType, snapshotDiff.ViewType);
                Assert.AreEqual(0.01, snapshotDiff.Map.Physical(0), 1e-9);
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }

        /// <summary>A compare file's map shows no ignition locks, and looking at it makes no IgnitionMaps folder next to the open bin.</summary>
        [TestMethod]
        public void CompareFileMakesNoIgnitionMapsFolder()
        {
            string open = Directory.CreateDirectory(Path.Combine(s_dir, "ign-open")).FullName;
            string theirs = Directory.CreateDirectory(Path.Combine(s_dir, "ign-other", "IgnitionMaps")).Parent!.FullName;
            string file = Path.Combine(open, "a.bin"), other = Path.Combine(theirs, "b.bin");
            File.Copy(Path.Combine(Here(), "..", "T5Binaries", StockBin), file, true);
            File.Copy(Path.Combine(Here(), "..", "T5Binaries", "4300331.bin"), other, true);
            s_session!.Dispatch(async () =>
            {
                var vm = new T5MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                await vm.CompareToFileAsync(other);
                var results = (CompareResultsViewModel)vm.SelectedViewer!;
                results.ShowDifferenceMap(results.Rows.SourceCollection.Cast<CompareRow>().Single(r => r.SymbolName == "Ign_map_0!"));
                Assert.AreEqual("Symbol difference: Ign_map_0! [b.bin]", ((MapViewerViewModel)vm.SelectedViewer!).Title);
                Assert.IsFalse(Directory.Exists(Path.Combine(open, "IgnitionMaps")));
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
