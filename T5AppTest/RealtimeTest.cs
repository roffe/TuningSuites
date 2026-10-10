using System;
using System.IO;
using System.Linq;
using Avalonia.VisualTree;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SuiteApp.ViewModels;
using T5App.ViewModels;
using T5App.Views;
using Trionic5Tools;

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

                // Configure realtime panel opens it without connecting; Switch mode then starts it (without an adapter it doesn't poll)
                vm.Settings.Adapter = "";
                vm.ConfigureRealtimePanelCommand.Execute(null);
                var rt = (T5RealtimeViewModel)vm.Realtime!;
                Assert.IsFalse(rt.HasStarted);
                await vm.ToggleRealtimePanelCommand.ExecuteAsync(null);
                Assert.AreSame(rt, vm.Realtime);
                Assert.IsTrue(rt.HasStarted);
                Assert.IsFalse(rt.IsRunning);
                Assert.AreEqual("Rpm", rt.Rows[0].Name);
                Assert.AreEqual("Realtime monitor", rt.Title);
                // peaks start at the row's minimum (below any value read); Add symbol offers SRAM symbols of 1 to 4 bytes only
                Assert.AreEqual(-1, rt.Rows.Single(r => r.Name == "P_medel").Peak);
                CollectionAssert.Contains(rt.SymbolNames.ToList(), "Rpm");
                CollectionAssert.DoesNotContain(rt.SymbolNames.ToList(), "Insp_mat!");
                Assert.IsNull(rt.Lookup("Insp_mat!"));

                // FillRealtimePool: going online reads the Fuel list (plus the user rows); each tab its own list
                CollectionAssert.AreEquivalent(new[] { "P_medel", "Lufttemp", "Kyl_temp", "Rpm", "Medeltrot", "Regl_tryck", "Pgm_status", "AD_sond",
                    "Insptid_ms10", "Lacc_mangd", "Acc_mangd", "Lret_mangd", "Ret_mangd" }, rt.PolledNames.ToArray());
                rt.SelectedTab = (int)T5RealtimeTab.Boost;
                CollectionAssert.AreEquivalent(new[] { "P_medel", "Lufttemp", "Kyl_temp", "Rpm", "Medeltrot", "Regl_tryck", "Pgm_status", "AD_sond",
                    "Max_tryck", "Apc_decrese", "P_fak", "I_fak", "D_fak", "PWM_ut10" }, rt.PolledNames.ToArray());
                rt.SelectedTab = (int)T5RealtimeTab.AutotuneIgnition;
                CollectionAssert.AreEquivalent(new[] { "P_medel", "Rpm", "Knock_offset1234", "Pgm_status", "AD_sond" }, rt.PolledNames.ToArray());
                rt.SelectedTab = (int)T5RealtimeTab.UserMaps;   // keeps the previous list
                CollectionAssert.AreEquivalent(new[] { "P_medel", "Rpm", "Knock_offset1234", "Pgm_status", "AD_sond" }, rt.PolledNames.ToArray());
                rt.SelectedTab = (int)T5RealtimeTab.Dashboard;
                // a row removed from the table is still read where a tab's list has it
                rt.Remove([rt.Rows.Single(r => r.Name == "Regl_tryck")]);
                CollectionAssert.Contains(rt.PolledNames.ToList(), "Regl_tryck");

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
                Assert.AreEqual(250, rt.PeakTorque);
                Assert.AreEqual(3000, rt.PeakTorqueRpm);
                Assert.AreEqual(0.8, rt.PeakBoost);
                Assert.IsTrue(rt.IsClosedLoopLed && rt.IsWarmupLed && !rt.IsIdleLed);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Save(window, "realtime");

                // a pass without the dashboard symbols (another tab): the displays keep their last values
                rt.Apply(new RealtimeSample(DateTime.Now, [("Rpm", 3100), ("Pgm_status", 0x02000000)], 20, null));
                Assert.AreEqual(250, rt.Torque);
                Assert.AreEqual(90, rt.Speed);
                Assert.AreEqual(3100, rt.Rpm);

                // the knock tab: counts, the increase since the last change, offsets and their peaks
                rt.SelectedTab = (int)T5RealtimeTab.Knock;
                rt.Apply(new RealtimeSample(DateTime.Now, [("Knock_count_cyl1", 5), ("Knock_offset1", 1.5)], 20, null));
                rt.Apply(new RealtimeSample(DateTime.Now, [("Knock_count_cyl1", 8), ("Knock_offset1", 0.5)], 20, null));
                Assert.AreEqual(8, rt.Cylinders[0].Count);
                Assert.AreEqual(3, rt.Cylinders[0].Delta);
                Assert.IsTrue(rt.Cylinders[0].HasDelta);
                Assert.AreEqual(1.5, rt.Cylinders[0].PeakOffset);
                Assert.AreEqual(0, rt.Cylinders[1].PeakOffset);
                foreach (var tab in new[] { T5RealtimeTab.Fuel, T5RealtimeTab.Ignition, T5RealtimeTab.Boost, T5RealtimeTab.Knock, T5RealtimeTab.Dashboard })
                {
                    rt.SelectedTab = (int)tab;
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    Save(window, "rt-" + tab);
                    rt.IsNight = true;
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    Save(window, "night-" + tab);
                    rt.IsNight = false;
                }

                // the Engine status LEDs follow Pgm_status, bit 37 included; the Settings switches wait for the ECU
                rt.Apply(new RealtimeSample(DateTime.Now, [("Pgm_status", 0x2000000010L | 0x02000000)], 20, null));
                Assert.AreEqual(40, rt.StatusLeds.Count);
                Assert.IsTrue(rt.StatusLeds.Single(l => l.Caption == "Engine is warm").IsOn);
                Assert.IsTrue(rt.StatusLeds.Single(l => l.Caption == "Active lambda control").IsOn);
                Assert.IsTrue(rt.StatusLeds.Single(l => l.Caption == "Enrichment after fuelcut").IsOn);
                Assert.IsFalse(rt.StatusLeds.Single(l => l.Caption == "Fuel cut").IsOn);
                Assert.AreEqual(15, rt.Toggles.Count);
                var tabs = window.GetVisualDescendants().OfType<Avalonia.Controls.TabControl>().First(t => t.Name == "Tabs");
                tabs.SelectedIndex = (int)T5RealtimeTab.EngineStatus;
                Assert.AreEqual((int)T5RealtimeTab.EngineStatus, rt.SelectedTab);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Save(window, "enginestatus");
                tabs.SelectedIndex = (int)T5RealtimeTab.OnlineGraph;
                var g0 = DateTime.Now;
                for (int i = 0; i < 20; i++) rt.Apply(new RealtimeSample(g0.AddMilliseconds(i * 50), [("Rpm", 1000 + i * 200), ("P_medel", i * 0.05 - 0.5), ("Pgm_status", 0)], 20, null));
                var (channels, start) = rt.GraphChannels();
                Assert.AreEqual(g0, start);
                Assert.AreEqual(0.95, channels[0].Time[^1], 1e-9);
                // the line selection: a hidden line stays hidden (settings), back on with a second click
                rt.ToggleGraphLine("TQ");
                Assert.AreEqual(8, rt.GraphChannels().Channels.Count);
                Assert.AreEqual("TQ", vm.T5Settings.HiddenGraphLines);
                rt.ToggleGraphLine("TQ");
                Assert.AreEqual(9, rt.GraphChannels().Channels.Count);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Save(window, "rt-graph");
                tabs.SelectedIndex = 0;

                // the wideband through AD_EGR fills the AFR feedback map at 3000 rpm / 0.8 bar; the AFR viewers
                vm.Settings.UseWidebandLambda = true;
                vm.Settings.WideBandSymbol = "AD_EGR";
                // Settings Ok: the panel reads the new lambda input (FillRealtimePool read the setting each time)
                vm.T5SettingsChanged();
                Assert.IsTrue(rt.Rows.Any(r => r.Name == "AD_EGR") && rt.Rows.All(r => r.Name != "AD_sond"));
                CollectionAssert.Contains(rt.PolledNames.ToList(), "AD_EGR");
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
