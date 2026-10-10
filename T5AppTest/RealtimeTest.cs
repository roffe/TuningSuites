using System;
using System.IO;
using System.Linq;
using Avalonia.VisualTree;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SuiteApp.ViewModels;
using T5App.ViewModels;
using T5App.Views;

namespace T5AppTest
{
    public partial class AppTest
    {
        [TestMethod]
        public void RealtimePanelAndLogs()
        {
            string file = CopyOfStockBin("rt.BIN");
            string log = Path.Combine(s_dir, "rt-20261010-CanTraceExt.t5l");
            var t0 = new DateTime(2026, 10, 10, 12, 0, 0);
            File.WriteAllLines(log, Enumerable.Range(0, 50).Select(i =>
                RealtimeLog.Line(t0.AddMilliseconds(i * 100), [("Rpm", 900 + i * 40), ("P_medel", i * 0.02)], false)));
            s_session!.Dispatch(async () =>
            {
                var vm = new T5MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));

                // the panel opens; without an adapter it doesn't poll
                vm.Settings.Adapter = "";
                await vm.ToggleRealtimePanelCommand.ExecuteAsync(null);
                RealtimeViewModel rt = vm.Realtime!;
                Assert.IsFalse(rt.IsRunning);
                Assert.AreEqual("Rpm", rt.Rows[0].Name);

                // a pass on screen: T5's names on the dashboard, the Pgm_status texts
                rt.Apply(new RealtimeSample(DateTime.Now,
                [
                    ("Rpm", 3000), ("P_medel", 0.8), ("TQ", 250), ("Medeltrot", 60), ("Kyl_temp", 88), ("Lufttemp", 25), ("Bil_hast", 90),
                    ("Ign_angle", 22.5), ("Knock_offset1234", 1.5), ("Pgm_status", 0x02000000), ("AD_sond", 0.98), ("LoadAccCyl2", 7),
                ], 20, null));
                Assert.AreEqual(3000, rt.Rpm);
                Assert.AreEqual(0.8, rt.Boost);
                Assert.AreEqual(250, rt.Torque);
                Assert.AreEqual(88, rt.Coolant);
                Assert.AreEqual(CommonSuite.Realtime.Power(3000, 250), rt.Power);
                Assert.AreEqual("Closed loop", rt.LambdaStatus);
                Assert.AreEqual("No fuelcut", rt.FuelcutStatus);
                Assert.AreEqual(0.98, rt.Lambda);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Save(window, "realtime");

                // the Engine status LEDs follow Pgm_status, bit 37 included; the Settings switches wait for the ECU
                rt.Apply(new RealtimeSample(DateTime.Now, [("Pgm_status", 0x2000000010L | 0x02000000)], 20, null));
                Assert.AreEqual(40, rt.StatusLeds.Count);
                Assert.IsTrue(rt.StatusLeds.Single(l => l.Caption == "Engine is warm").IsOn);
                Assert.IsTrue(rt.StatusLeds.Single(l => l.Caption == "Active lambda control").IsOn);
                Assert.IsTrue(rt.StatusLeds.Single(l => l.Caption == "Enrichment after fuelcut").IsOn);
                Assert.IsFalse(rt.StatusLeds.Single(l => l.Caption == "Fuel cut").IsOn);
                Assert.AreEqual(15, rt.Toggles.Count);
                var tabs = window.GetVisualDescendants().OfType<Avalonia.Controls.TabControl>().First(t => t.Name == "Tabs");
                tabs.SelectedIndex = 4;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Save(window, "enginestatus");
                tabs.SelectedIndex = 0;

                // the wideband through AD_EGR fills the AFR feedback map at 3000 rpm / 0.8 bar; the AFR viewers
                vm.Settings.UseWidebandLambda = true;
                vm.Settings.WideBandSymbol = "AD_EGR";
                for (int i = 0; i < 3; i++)
                    rt.Apply(new RealtimeSample(DateTime.Now, [("Rpm", 3000), ("P_medel", 0.8), ("AD_EGR", 12.5), ("Pgm_status", 0)], 20, null));
                vm.ShowAfrCommand.Execute("FeedbackAFR");
                var feedback = (MapViewerViewModel)vm.SelectedViewer!;
                Assert.AreEqual("FeedbackAFR", feedback.MapName);
                Assert.IsTrue(feedback.IsReadOnly);
                Assert.AreEqual(1, Enumerable.Range(0, feedback.Map.Count).Count(i => feedback.Map[i] != 0));
                Assert.AreEqual(12.5, Enumerable.Range(0, feedback.Map.Count).Select(feedback.Map.Physical).Max(), 0.1);
                vm.ShowAfrCommand.Execute("TargetAFR");
                var target = (MapViewerViewModel)vm.SelectedViewer!;
                Assert.IsFalse(target.IsReadOnly);
                // T5Suite's default target: 13.0 below 1000 rpm, richer with boost
                Assert.AreEqual(256, target.Map.Count);
                Assert.AreEqual(13.0, target.Map.Physical(0), 0.05);
                Assert.IsTrue(target.Map.Physical(255) < 13);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Save(window, "afrtarget");

                // the ignition autotune's lock map (nothing locked yet) and the autotune settings
                vm.ShowIgnitionLocksCommand.Execute(null);
                var locks = (MapViewerViewModel)vm.SelectedViewer!;
                Assert.AreEqual("IgnitionLockMap", locks.MapName);
                Assert.IsTrue(Enumerable.Range(0, locks.Map.Count).All(i => locks.Map[i] == 0));
                var autotune = new AutotuneSettingsViewModel(vm.Settings, vm.T5Settings);
                Assert.AreEqual(35m, autotune.GlobalMaximumAdvance);
                autotune.GlobalMaximumAdvance = 33.5m;
                var autotuneWindow = new AutotuneSettingsWindow { DataContext = autotune };
                autotuneWindow.Show();
                Save(autotuneWindow, "autotunesettings");
                autotuneWindow.Close();
                autotune.Apply(vm.Settings, vm.T5Settings);
                Assert.AreEqual(33.5, vm.T5Settings.GlobalMaximumIgnitionAdvance);

                // a .t5l in the viewer
                await vm.OpenLogAsync(log, _ => System.Threading.Tasks.Task.FromResult<int?>(0));
                var viewer = (LogViewerViewModel)vm.SelectedViewer!;
                Assert.AreEqual("CANBus logfile: rt-20261010-CanTraceExt.t5l", viewer.Title);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Save(window, "logviewer");
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
