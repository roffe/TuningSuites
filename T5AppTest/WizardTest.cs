using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using T5App.ViewModels;
using T5App.Views;
using Trionic5Tools;

namespace T5AppTest
{
    public partial class AppTest
    {
        [TestMethod]
        public void TuningWizards()
        {
            string file = CopyOfStockBin("wizards.BIN");
            s_session!.Dispatch(async () =>
            {
                var vm = new T5MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));

                TuneMeUpViewModel tune = vm.TuneMeUp()!;
                Assert.AreEqual(1, tune.Stage);
                var tuneWindow = new TuneMeUpWindow { DataContext = tune };
                tuneWindow.Show();
                Save(tuneWindow, "tunemeup");
                tuneWindow.Close();
                Assert.IsTrue((await vm.RunTuneMeUpAsync(tune))!.Count > 0);
                Assert.AreEqual(TuningStage.Stage1, T5Binary.Open(file).File.GetTrionicProperties().TuningStage);

                InjectorWizardViewModel injectors = vm.InjectorWizard()!;
                injectors.Type = InjectorType.GreenGiants;
                var injWindow = new InjectorWizardWindow { DataContext = injectors };
                injWindow.Show();
                Save(injWindow, "injectorwizard");
                injWindow.Close();
                await vm.ApplyInjectorsAsync(injectors);
                Assert.AreEqual(InjectorType.GreenGiants, T5Binary.Open(file).File.GetTrionicProperties().InjectorType);
                Assert.AreEqual("Checksum: OK", vm.ChecksumText);

                // a 3.0 bar file: the boost map and its MAP axis show × 1.2, an edit goes back rounded up
                await vm.ConvertMapSensorAsync(MapSensorType.MapSensor30);
                vm.OpenSymbolByName("Tryck_mat!");
                var boost = (SuiteApp.ViewModels.MapViewerViewModel)vm.SelectedViewer!;
                Assert.AreEqual(120, boost.Map.ScalePercent);
                int raw = boost.Map[0];
                Assert.AreEqual((raw * 120 / 100).ToString(), boost.Map.FormatCell(0, MapControls.MapViewType.Decimal));
                Assert.IsTrue(boost.Map.TryParse("121", MapControls.MapViewType.Decimal, out int back, out _));
                Assert.AreEqual(101, back);
                // the injection map: unsigned, with the open-loop limits when lambda control is on
                vm.OpenSymbolByName("Insp_mat!");
                var fuel = (SuiteApp.ViewModels.MapViewerViewModel)vm.SelectedViewer!;
                Assert.AreEqual(0, fuel.Map.MinRaw);
                Assert.AreEqual(fuel.Map.Rows, fuel.Map.OpenLoop?.Length ?? fuel.Map.Rows);
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
