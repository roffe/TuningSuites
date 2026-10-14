using System;
using System.Linq;
using MapControls;
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
        // the data indices of a map whose marks pass, and those of the cells (data row, column) expected
        private static int[] Marked(MapData map, Func<CellMarks, bool> mark) => [.. Enumerable.Range(0, map.Count).Where(i => mark(map.Marks!(i)))];

        private static int[] Cells(MapData map, Func<int, int, bool> cell) => [.. Enumerable.Range(0, map.Count).Where(i => cell(i / map.Cols, i % map.Cols))];

        [TestMethod]
        public void FuelAndIgnitionMapsShowT5SuitesMarks()
        {
            string file = CopyOfStockBin("marks.bin");
            s_session!.Dispatch(async () =>
            {
                var vm = new MainViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));

                // 4239273: Open_loop! 76 80 200 200 153×5 128 110 108 108 96×3, Knock_press_tab! 70×7 65 60×8, Kadapt load 85..105 at
                // 2100..3250 rpm, the boost adaption range unset (manual 2750..4000)
                vm.OpenSymbolByName("Insp_mat!");
                var fuel = (MapViewerViewModel)vm.SelectedViewer!;
                Assert.AreEqual(OpenLoopMark.Box, fuel.OpenLoopMark, "T5Suite always drew the closed-loop box");
                // the Easy view's MAP axis in bar as T5Suite's (raw × 0.01 − 1 on the 2.5 bar sensor), the Decimal view's raw
                CollectionAssert.AreEqual(new double[] { 22, 30, 38 }, fuel.Map.XAxis![..3]);
                Assert.AreEqual(-0.78, fuel.Map.XEasy![0], 1e-9);
                Assert.AreEqual(1.08, fuel.Map.XEasy![15], 1e-9);
                Assert.AreEqual(16 * 16, fuel.Map.Count);
                CollectionAssert.AreEqual(Cells(fuel.Map, (r, c) => c == 8 && r is >= 5 and <= 7), Marked(fuel.Map, m => m.KnockAdaption));
                CollectionAssert.AreEqual(Cells(fuel.Map, (r, c) => c == 15 && r is >= 6 and <= 9), Marked(fuel.Map, m => m.BoostAdaption));
                Assert.IsEmpty(Marked(fuel.Map, m => m.Locked || m.Samples > 0));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Save(window, "marks-fuel");

                // Ignition lock map opens Ign_map_0!: padlocks below the knock pressure (by row), nothing locked yet
                vm.T5.ShowIgnitionLocksCommand.Execute(null);
                var ign = (MapViewerViewModel)vm.SelectedViewer!;
                Assert.AreEqual("Ign_map_0!", ign.MapName);
                Assert.AreEqual(18, ign.Map.Cols);
                Assert.AreEqual(-1.0, ign.Map.XEasy![0], 1e-9);
                Assert.AreEqual(1.4, ign.Map.XEasy![17], 1e-9);
                // a "Pressure error (bar)" axis × 0.01 without the − 1; the boost regulator's maps have no marks
                vm.OpenSymbolByName("P_fors!");
                var pFors = (MapViewerViewModel)vm.SelectedViewer!;
                CollectionAssert.AreEqual(pFors.Map.XAxis!.Select(v => v * 0.01).ToArray(), pFors.Map.XEasy);
                Assert.IsNull(pFors.Map.Marks);
                vm.SelectedViewer = ign;
                int[] belowKnock = Cells(ign.Map, (r, c) => r <= 7 ? c <= 6 : c <= 5);
                CollectionAssert.AreEqual(belowKnock, Marked(ign.Map, m => m.Locked));
                CollectionAssert.AreEqual(Cells(ign.Map, (r, c) => c == 8 && r is >= 5 and <= 7), Marked(ign.Map, m => m.KnockAdaption));
                CollectionAssert.AreEqual(Cells(ign.Map, (r, c) => c == 17 && r is >= 6 and <= 9), Marked(ign.Map, m => m.BoostAdaption));
                // a cell the autotune locks shows at once, in its own row (T5Suite drew the lock map upside down)
                vm.T5.IgnitionMaps!.LockCell(10, 12);
                CollectionAssert.AreEqual(belowKnock.Append(12 * 18 + 10).Order().ToArray(), Marked(ign.Map, m => m.Locked));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Save(window, "marks-ignition");
                vm.T5.ReleaseIgnitionLocksCommand.Execute(null);
                CollectionAssert.AreEqual(belowKnock, Marked(ign.Map, m => m.Locked));
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
