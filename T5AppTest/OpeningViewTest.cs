using System.IO;
using System.Linq;
using CommonSuite;
using MapControls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SuiteApp.ViewModels;
using T5App.ViewModels;
using T5App.Views;
using Trionic5Tools;

namespace T5AppTest
{
    public partial class AppTest
    {
        private static string KnockSnapshot(string name)
        {
            var data = new byte[576];
            data[1] = 3;
            string file = Path.Combine(s_dir, name);
            File.WriteAllText(file, System.Convert.ToHexString(data));
            return file;
        }

        /// <summary>
        /// T5Suite's MapViewerFactory.Get(settings, file): Decimal when that is the default, Easy for any other (Hex too), for the
        /// symbol viewers, the AFR maps and the knock counter snapshot alike.
        /// </summary>
        [TestMethod]
        public void MapsOpenInDecimalOrEasyAsT5Suite()
        {
            string file = CopyOfStockBin("openview.bin");
            s_session!.Dispatch(async () =>
            {
                var vm = new T5MainWindowViewModel();
                SuiteViewType before = vm.Settings.DefaultViewType;
                try
                {
                    var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                    window.Show();
                    Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));

                    vm.Settings.DefaultViewType = SuiteViewType.Hexadecimal;
                    vm.OpenSymbolByName("Ign_map_0!");
                    Assert.AreEqual(MapViewType.Easy, ((MapViewerViewModel)vm.SelectedViewer!).ViewType);
                    vm.ShowAfrCommand.Execute("TargetAFR");
                    Assert.AreEqual(MapViewType.Easy, ((MapViewerViewModel)vm.SelectedViewer!).ViewType);
                    // the knock counters on the MAP axis in bar
                    vm.ShowKnockMap(KnockSnapshot("openview-hex.KNK"));
                    var knock = (MapViewerViewModel)vm.SelectedViewer!;
                    Assert.AreEqual(MapViewType.Easy, knock.ViewType);
                    Assert.AreEqual(-1, knock.Map.XEasy![0], 1e-9);

                    vm.Settings.DefaultViewType = SuiteViewType.Decimal;
                    vm.OpenSymbolByName("Insp_mat!");
                    Assert.AreEqual(MapViewType.Decimal, ((MapViewerViewModel)vm.SelectedViewer!).ViewType);
                    vm.ShowAfrCommand.Execute("FeedbackAFR");
                    Assert.AreEqual(MapViewType.Decimal, ((MapViewerViewModel)vm.SelectedViewer!).ViewType);
                    vm.ShowKnockMap(KnockSnapshot("openview-dec.KNK"));
                    Assert.AreEqual(MapViewType.Decimal, ((MapViewerViewModel)vm.SelectedViewer!).ViewType);
                    window.Close();
                }
                finally
                {
                    vm.Settings.DefaultViewType = before;
                }
                return true;
            }, default).GetAwaiter().GetResult();
        }

        /// <summary>On a 3.0 bar sensor the knock counter snapshot's MAP axis is scaled as Ign_map_0!'s (Decimal3Bar), Hex stored.</summary>
        [TestMethod]
        public void KnockMapAxisFollowsTheSensor()
        {
            string file = CopyOfStockBin("knock3bar.bin");
            T5Tuning.ConvertMapSensor(T5Binary.Open(file), MapSensorType.MapSensor25, MapSensorType.MapSensor30, true);
            s_session!.Dispatch(async () =>
            {
                var vm = new T5MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                vm.OpenSymbolByName("Ign_map_0!");
                var ign = (MapViewerViewModel)vm.SelectedViewer!;
                vm.ShowKnockMap(KnockSnapshot("knock3bar.KNK"));
                var knock = (MapViewerViewModel)vm.SelectedViewer!;
                StringAssert.StartsWith(knock.Title, "Knock counter snapshot");
                CollectionAssert.AreEqual(ign.Map.XAxis, knock.Map.XAxis);
                CollectionAssert.AreEqual(ign.Map.XHex, knock.Map.XHex);
                CollectionAssert.AreEqual(ign.Map.XEasy, knock.Map.XEasy);
                CollectionAssert.AreNotEqual(knock.Map.XHex, knock.Map.XAxis);
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
