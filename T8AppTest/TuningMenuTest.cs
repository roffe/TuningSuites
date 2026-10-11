using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SuiteApp.ViewModels;
using SuiteApp.Views;
using T8App.ViewModels;
using T8App.Views;

namespace T8AppTest
{
    public partial class AppTest
    {
        private static string[] Headers(MenuItem menu) => menu.Items.OfType<MenuItem>().Select(i => (string)i.Header!).ToArray();

        private static MenuItem TuningEntry(MenuItem menu, params string[] path)
        {
            foreach (string caption in path) menu = menu.Items.OfType<MenuItem>().Single(i => (string?)i.Header == MenuTextConverter.Escape(caption));
            return menu;
        }

        /// <summary>
        /// The Tuning menu is txlogger's T8 menu after the wizard: the newer calibration's shared limiters in place of the old one's
        /// manual and automatic ones, the latest BioPower software's jerk enrichment by temperature.
        /// </summary>
        [TestMethod]
        public void TuningMenuIsTxloggersWithTheBinsMaps()
        {
            string old = CopyOfStockBin("tuning.bin");
            string Stock(string name, string stock)
            {
                string file = Path.Combine(s_dir, name);
                File.Copy(Path.Combine(Here(), "..", "T8Binaries", stock), file, true);
                return file;
            }
            string fe = Stock("tuning-fe.bin", "55567225_FE09_C_FME2_96_FIEF_82v.bin"), ff = Stock("tuning-ff.bin", "55567225_FF0L_C_FMEP_24_FMEF_85b.bin");
            s_session!.Dispatch(async () =>
            {
                var vm = new T8MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(old, true));
                var tuning = window.View.FindControl<MenuItem>("QuickMapsMenu")!;
                CollectionAssert.AreEqual(new[] { "Tuning Wizard", "Airmass", "Torque", "Injectors", "Fuel", "Boost", "Ignition", "Pedal" }, Headers(tuning));
                string Opened(MenuItem item)
                {
                    item.Command!.Execute(item.CommandParameter);
                    return ((MapViewerViewModel)vm.SelectedViewer!).MapName;
                }
                Assert.AreEqual("AirCtrlCal.Ppart_BoostMap", Opened(TuningEntry(tuning, "Boost", "P factor")));
                Assert.AreEqual("BstKnkCal.MaxAirmassAu", Opened(TuningEntry(tuning, "Airmass", "Max airmass map (auto)")));
                Assert.AreEqual("TrqLimCal.Trq_MaxEngineManTab2", Opened(TuningEntry(tuning, "Torque", "Trq limit manual 150hp")));
                // no AirCtrlCal.AirmassLimiter in any bin, no newer calibration or E85 limiters in an old one
                CollectionAssert.AreEqual(new[] { "Max airmass map (manual)", "Max airmass map (auto)", "Airmass Fuelcut", "AirCtrlCal.PRatioMaxTab" },
                    Headers(TuningEntry(tuning, "Airmass")));
                CollectionAssert.AreEqual(new[] { "Nominal torque map", "Airmass torque map", "Ambient pressure trq limiter", "Trq limit in overboost",
                    "Trq limit manual 150hp", "Trq limit manual 175+hp", "Trq limit auto 150hp", "Trq limit auto 175+hp", "Manual gear trq limit", "RPM limiter" },
                    Headers(TuningEntry(tuning, "Torque")));
                // Rescale AccPedalMap has no T8Suite feature
                CollectionAssert.AreEqual(new[] { "Pedal position map", "Torque request map" }, Headers(TuningEntry(tuning, "Pedal")));

                // FE: one limiter and max airmass map for both gearboxes, shown once; its E85 limiters stay out (FlexFuel off)
                Assert.IsTrue(await vm.OpenPlainFileAsync(fe, true));
                Assert.AreEqual("TrqLimCal.Trq_MaxEngineTab2", Opened(TuningEntry(tuning, "Torque", "Trq limit manual 150hp")));
                Assert.AreEqual("TrqLimCal.Trq_MaxEngineTab1", Opened(TuningEntry(tuning, "Torque", "Trq limit manual 175+hp")));
                CollectionAssert.AreEqual(new[] { "Nominal torque map", "Airmass torque map", "Ambient pressure trq limiter", "Trq limit in overboost",
                    "Trq limit manual 150hp", "Trq limit manual 175+hp", "Manual gear trq limit", "RPM limiter" }, Headers(TuningEntry(tuning, "Torque")));
                CollectionAssert.AreEqual(new[] { "Max airmass map (manual)", "Airmass Fuelcut", "AirCtrlCal.PRatioMaxTab" }, Headers(TuningEntry(tuning, "Airmass")));

                // FF0L: jerk enrichment by temperature, the E85 limiters, no FlexFuel switch
                Assert.IsTrue(await vm.OpenPlainFileAsync(ff, true));
                var jerk = TuningEntry(tuning, "Fuel", "Jerk enrichment petrol");
                Assert.AreEqual("BFuelCal.m_AirJerkMAP", ToolTip.GetTip(jerk));
                Assert.AreEqual("BFuelCal.m_AirJerkMAP", Opened(jerk));
                Assert.AreEqual("BFuelCal.JerkEnrichFacTAB", Opened(TuningEntry(tuning, "Fuel", "Jerk enrichment Fuelmaster")));
                Assert.AreEqual("FFTrqCal.FFTrq__MaxEngineTab1", Headers(TuningEntry(tuning, "Torque"))[^2]);
                CollectionAssert.DoesNotContain(Headers(TuningEntry(tuning, "Fuel")), "FFFuelCal.ST__enable");
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
