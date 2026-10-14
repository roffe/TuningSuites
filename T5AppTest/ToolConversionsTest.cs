using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using CommonSuite;
using MapControls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TxSuite.ViewModels;
using TxSuite.Views;
using T5App.ViewModels;
using T5App.Views;

namespace T5AppTest
{
    public partial class AppTest
    {
        /// <summary>
        /// frmInjectionTiming: the duty cycle to two decimals of T5Suite's float (40.76 %, not 40.80), shown without trailing zeros,
        /// and the headers in bar from the float MAP value (raw 38 on a 3.0 bar sensor is -0.54, not -0.55).
        /// </summary>
        [TestMethod]
        public void InjectionTimingAsT5Suite()
        {
            string file = CopyOfStockBin("timing.bin"), threeBar = CopyOfStockBin("timing3bar.bin");
            using (var fs = new FileStream(threeBar, FileMode.Open))
            {
                // the 3.0 bar marker, as the MAP sensor conversion writes it
                fs.Position = fs.Length - 0x1FF;
                fs.WriteByte(0x01);
            }
            s_session!.Dispatch(async () =>
            {
                var vm = new MainViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));

                // Normal, 60 °C, 13 V
                MapData timing = vm.T5.InjectionTiming("Insp_mat!", 46, 13, vm.T5.InjectorConstant, true)!;
                int Cell(int rpm, int map) => Array.IndexOf(timing.YAxis!, (double)rpm) * timing.Cols + Array.IndexOf(timing.XAxis!, (double)map);
                Assert.AreEqual(40.76, timing.Physical(Cell(6200, 96)), 1e-9);
                Assert.AreEqual(77.05, timing.Physical(Cell(4300, 208)), 1e-9);
                Assert.AreEqual(1.36, timing.Physical(Cell(900, 22)), 1e-9);
                Assert.AreEqual(40.76.ToString(CultureInfo.CurrentCulture), timing.FormatCell(Cell(6200, 96), MapViewType.Easy));
                int round = Enumerable.Range(0, timing.Count).First(i => timing[i] % 10 == 0 && timing[i] % 100 != 0);
                Assert.AreEqual((timing[round] / 100.0).ToString(CultureInfo.CurrentCulture), timing.FormatCell(round, MapViewType.Easy));
                // a duty cycle past the 16-bit cell's 655.35 % still shows (-30 °C, 4 V and the dialog's injector constant up to 255
                // reach the model's 130 ms, 671.67 % at 6200 rpm)
                MapData rich = vm.T5.InjectionTiming("Insp_mat!", 230, 4, 100, true)!;
                Assert.IsTrue(Enumerable.Range(0, rich.Count).Any(i => double.Parse(rich.FormatCell(i, MapViewType.Easy), CultureInfo.CurrentCulture) > 655.35));

                // the bar headers on every map: raw × 0.01 − 1 on the stock sensor
                foreach (var (_, map) in T5App.ViewModels.T5Module.InjectionConditions)
                {
                    MapData m = vm.T5.InjectionTiming(map, 46, 13, vm.T5.InjectorConstant, false)!;
                    CollectionAssert.AreEqual(vm.Binary!.GetXaxisValues(map).Select(v => Math.Round(v * 0.01 - 1, 9)).ToArray(),
                        m.XEasy!.Select(v => Math.Round(v, 9)).ToArray(), map);
                }

                Assert.IsTrue(await vm.OpenPlainFileAsync(threeBar, true));
                timing = vm.T5.InjectionTiming("Insp_mat!", 46, 13, vm.T5.InjectorConstant, false)!;
                int column = Array.IndexOf(vm.Binary!.GetXaxisValues("Insp_mat!"), 38);
                Assert.AreEqual(-0.544, timing.XEasy![column], 1e-9);
                Assert.AreEqual(45, timing.XAxis![column]);
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }

        /// <summary>frmBoostBiasWizard: "Result rpm from 2500 rpm upto" 2500 + step × 300, following the step.</summary>
        [TestMethod]
        public void BoostBiasWizardShowsTheRange()
        {
            string file = CopyOfStockBin("boostbias.bin");
            s_session!.Dispatch(async () =>
            {
                var vm = new MainViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                Assert.AreEqual(10, vm.T5.BoostBiasStep());
                window.GetLogicalDescendants().OfType<MenuItem>().Single(m => m.Header as string == "Change boost bias range")
                    .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var wizard = window.OwnedWindows.Single();
                TextBlock Result() => wizard.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Text?.StartsWith("Result rpm") == true);
                Assert.AreEqual("Result rpm from 2500 rpm upto 5500", Result().Text);
                wizard.GetLogicalDescendants().OfType<NumericUpDown>().Single().Value = 12;
                Assert.AreEqual("Result rpm from 2500 rpm upto 6100", Result().Text);
                Save(wizard, "boostbias");
                wizard.Close();
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }

        /// <summary>
        /// T5Suite's Export logfile to LogWorks: rpm as it is, ignition with one decimal, the rest with two, halves away from zero
        /// (T7Suite's three decimals: 3000.000, 12.250, 0.125).
        /// </summary>
        [TestMethod]
        public void LogWorksNumbersAsT5Suite()
        {
            string file = CopyOfStockBin("logworks.bin");
            string log = Path.Combine(s_dir, "logworks-20261009-CanTraceExt.t5l");
            var t0 = new DateTime(2026, 10, 9, 12, 0, 0);
            File.WriteAllLines(log, [RealtimeLog.Line(t0, [("Rpm", 3000), ("Ign_angle", 12.25), ("P_medel", 0.125), ("AD_sond", 0.95)], false)]);
            s_session!.Dispatch(async () =>
            {
                var vm = new MainViewModel();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                vm.ExportLogDif(log, vm.LogSelection(log));
                string[] dif = File.ReadAllLines(Path.ChangeExtension(log, ".dif"));
                foreach (string value in new[] { "0,3000", "0,12.3", "0,0.13", "0,0.95" }) CollectionAssert.Contains(dif, value);
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
