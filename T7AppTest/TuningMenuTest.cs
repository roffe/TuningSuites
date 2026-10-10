using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SuiteApp.ViewModels;
using SuiteApp.Views;
using T7App.ViewModels;
using T7App.Views;

namespace T7AppTest
{
    public partial class AppTest
    {
        private static string[] Headers(MenuItem menu) => menu.Items.OfType<MenuItem>().Select(i => (string)i.Header!).ToArray();

        private static MenuItem TuningEntry(MenuItem menu, params string[] path)
        {
            foreach (string caption in path) menu = menu.Items.OfType<MenuItem>().Single(i => (string?)i.Header == MenuTextConverter.Escape(caption));
            return menu;
        }

        /// <summary>The Tuning menu is txlogger's T7 menu: the maps by the name this bin has them under, T7Suite's tools, the rest left out.</summary>
        [TestMethod]
        public void TuningMenuIsTxloggersWithTheBinsMaps()
        {
            string file = Path.Combine(s_dir, "tuning.bin"), bio = Path.Combine(s_dir, "tuning-bio.bin");
            File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), file, true);
            File.Copy(Path.Combine(Here(), "..", "T7Binaries", "55562425.bin"), bio, true);
            s_session!.Dispatch(async () =>
            {
                var vm = new T7MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                var tuning = window.FindControl<MenuItem>("QuickMapsMenu")!;
                // no Myrtilos maps in a stock bin
                CollectionAssert.AreEqual(new[] { "Actions", "Diagnostics", "Calibration", "Injectors", "Airmass", "Fuel", "Ignition", "Boost", "Knock", "Limiters", "Adaption" },
                    Headers(tuning));
                string Opened(MenuItem item)
                {
                    item.Command!.Execute(item.CommandParameter);
                    return ((MapViewerViewModel)vm.SelectedViewer!).MapName;
                }
                Assert.AreEqual("BFuelCal.Map", Opened(TuningEntry(tuning, "Fuel", "VE map")));
                // EA1WF0 has start ignition by air temperature only
                var start = TuningEntry(tuning, "Ignition", "Ignition Start");
                Assert.AreEqual("IgnStartCal.fi_StartTab", ToolTip.GetTip(start));
                Assert.AreEqual("IgnStartCal.fi_StartTab", Opened(start));
                // no E85, gas or real / false knock maps, no overboost; J1979, PI area editor and Gear calculator have no T7Suite feature
                CollectionAssert.AreEqual(new[] { "Ignition map", "Ignition Idle", "Ignition Start", "Knock pull map", "Max knock pull" },
                    Headers(TuningEntry(tuning, "Ignition")));
                CollectionAssert.AreEqual(new[] { "DTC Reader", "KnkDetAdap.KnkCntMap", "MissfAdap.MissfCntMap" }, Headers(TuningEntry(tuning, "Diagnostics")));
                // the counters and the ethanol adaption live in the ECU: read from SRAM as the Realtime menu does, never the file
                Assert.AreSame(vm.ShowEcuMapCommand, TuningEntry(tuning, "Diagnostics", "KnkDetAdap.KnkCntMap").Command);
                Assert.AreEqual("KnkDetAdap.KnkCntMap", TuningEntry(tuning, "Diagnostics", "KnkDetAdap.KnkCntMap").CommandParameter);
                CollectionAssert.DoesNotContain(Headers(TuningEntry(tuning, "Limiters")), "Overboost");
                CollectionAssert.AreEqual(new[] { "Max load norm" }, Headers(TuningEntry(tuning, "Fuel", "Closed Loop")));

                // several maps: each in its own viewer, the parts the bin has
                var overview = TuningEntry(tuning, "Boost", "Boost regulation overview");
                overview.Command!.Execute(overview.CommandParameter);
                CollectionAssert.AreEqual(new[] { "BFuelCal.Map", "IgnStartCal.fi_StartTab", "BoostCal.RegMap", "BoostCal.PMap", "BoostCal.IMap", "BoostCal.DMap" },
                    vm.Viewers.OfType<MapViewerViewModel>().Select(v => v.MapName).ToArray());
                Assert.AreEqual("BoostCal.PMap", Opened(TuningEntry(tuning, "Boost", "P factor")));
                var common = TuningEntry(tuning, "Fuel", "Common for tuning");
                Assert.AreEqual("AdpFuelCal.T_AdaptLim\nFCutCal.ST_Enable\nLambdaCal.ST_Enable\nPurgeCal.ST_PurgeEnable\nTorqueCal.M_BrakeLimit", ToolTip.GetTip(common));

                // Firmware information: T7Suite's dialog
                CollectionAssert.AreEqual(new[] { "Firmware information" }, Headers(TuningEntry(tuning, "Actions")));
                TuningEntry(tuning, "Actions", "Firmware information").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var dialog = (FirmwareInfoWindow)window.OwnedWindows.Single();
                dialog.Close(false);
                Assert.AreEqual("ESP Calibration", Headers(TuningEntry(tuning, "Calibration"))[0]);

                // BioPower: its E85 VE map in place of the startup VE map, the E85 maps there
                Assert.IsTrue(await vm.OpenPlainFileAsync(bio, true));
                tuning = window.FindControl<MenuItem>("QuickMapsMenu")!;
                Assert.AreEqual("BFuelCal.E85Map", Opened(TuningEntry(tuning, "Fuel", "Startup / E85 VE map")));
                Assert.AreEqual("IgnE85Cal.fi_AbsMap", Opened(TuningEntry(tuning, "Ignition", "Ignition for E85")));
                CollectionAssert.AreEqual(new[] { "Max load norm", "Max load E85" }, Headers(TuningEntry(tuning, "Fuel", "Closed Loop")));
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
