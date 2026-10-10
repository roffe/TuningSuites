using System.Linq;
using MapControls;
using Trionic5Tools;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SuiteApp.ViewModels;
using T5App.ViewModels;
using T5App.Views;

namespace T5AppTest
{
    public partial class AppTest
    {
        [TestMethod]
        public void MapMathFollowsT5Suite()
        {
            string file = CopyOfStockBin("math.BIN");
            s_session!.Dispatch(async () =>
            {
                var vm = new T5MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                // MapViewerEx's Multiply 1.1 in Decimal view: 25*110/100 = 27, where T7Suite's rounded factor left it at 25
                vm.OpenSymbolByName("Insp_mat!");
                var fuel = (MapViewerViewModel)vm.SelectedViewer!;
                Assert.AreEqual(25, fuel.Map[0]);
                MapOps.Apply(fuel.Map, [0], MapViewType.Decimal, MapMath.Multiply, 1.1);
                Assert.AreEqual(27, fuel.Map[0]);
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }

        /// <summary>On a 3.0 bar sensor the Decimal headers show the scaled MAP axis, the Hex ones the stored values (T5Suite's).</summary>
        [TestMethod]
        public void HexHeadersStoredOnScaledAxes()
        {
            string file = CopyOfStockBin("hex3bar.BIN");
            T5Tuning.ConvertMapSensor(T5Binary.Open(file), MapSensorType.MapSensor25, MapSensorType.MapSensor30, true);
            s_session!.Dispatch(async () =>
            {
                var vm = new T5MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                vm.OpenSymbolByName("Ign_map_0!");
                var ign = (MapViewerViewModel)vm.SelectedViewer!;
                double[] stored = [.. ign.Binary.GetXaxisValues("Ign_map_0!").Select(v => (double)v)];
                CollectionAssert.AreEqual(stored, ign.Map.XHex);
                CollectionAssert.AreEqual(stored.Select(v => (double)((int)v * 120 / 100)).ToArray(), ign.Map.XAxis);
                CollectionAssert.AreNotEqual(ign.Map.XAxis, ign.Map.XHex);
                // the AFR maps on the fuel map's axis follow the sensor too (T5Suite's Decimal3Bar headers)
                vm.ShowAfrCommand.Execute("TargetAFR");
                var afr = (MapViewerViewModel)vm.SelectedViewer!;
                double[] fuelAxis = [.. afr.Binary.GetXaxisValues("Insp_mat!").Select(v => (double)v)];
                CollectionAssert.AreEqual(fuelAxis, afr.Map.XHex);
                CollectionAssert.AreEqual(fuelAxis.Select(v => (double)((int)v * 120 / 100)).ToArray(), afr.Map.XAxis);
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
