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
        public void FirmwareOptions()
        {
            string file = CopyOfStockBin("firmware.BIN");
            s_session!.Dispatch(async () =>
            {
                var vm = new T5MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                StringAssert.StartsWith(vm.OpenClosedText, "T5.5 | ");

                FirmwareOptionsViewModel options = vm.FirmwareOptions()!;
                Assert.AreEqual("4239273", options.Partnumber.Trim());
                OptionFlag fuelcut = options.Fuelling.Single(f => f.Caption == "Fuelcut in engine brake");
                // frmEasyFirmwareSettings' captions, turbo by name (its list was one off)
                CollectionAssert.AreEqual(new[] { "Stock", "Green giants", "Siemens 630cc/min", "Siemens 875cc/min", "Siemens 1000cc/min" },
                    FirmwareOptionsViewModel.Injectors.Select(i => FirmwareOptionsViewModel.Caption(i)).ToArray());
                Assert.AreEqual("5.0 bar sensor", FirmwareOptionsViewModel.Caption(MapSensorType.MapSensor50));
                Assert.AreEqual("TD04-15T", FirmwareOptionsViewModel.Caption(CommonSuite.TurboType.TD0415T));
                Assert.AreEqual("GT17", FirmwareOptionsViewModel.Caption(CommonSuite.TurboType.GT17));
                Assert.AreEqual("Stock|Stage 1|Stage 8|Stage X", string.Join("|", new[] { TuningStage.Stock, TuningStage.Stage1, TuningStage.Stage8, TuningStage.StageX }
                    .Select(s => FirmwareOptionsViewModel.Caption(s))));
                bool was = fuelcut.IsChecked;
                fuelcut.IsChecked = !was;
                options.Injector = InjectorType.Siemens630Dekas;
                var dialog = new FirmwareOptionsWindow { DataContext = options };
                dialog.Show();
                Save(dialog, "firmwareoptions");
                dialog.Close();
                await vm.ApplyFirmwareAsync(options.Properties);

                Trionic5Properties after = T5Binary.Open(file).File.GetTrionicProperties();
                Assert.AreEqual(!was, after.Fuelcut);
                Assert.AreEqual(InjectorType.Siemens630Dekas, after.InjectorType);
                Assert.AreEqual("Checksum: OK", vm.ChecksumText);
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
