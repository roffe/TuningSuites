using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SuiteApp.Services;
using TxSuite.ViewModels;
using TxSuite.Views;
using T5App.ViewModels;
using T5App.Views;
using Trionic5Tools;

namespace T5AppTest
{
    public partial class AppTest
    {
        /// <summary>The T5 manual's shots (t5-manual/*.qmd, named as its screenshot markers), on a main window of their own.</summary>
        private static async Task ManualScreenshots(string file, ThemeVariant theme, string suffix)
        {
            var vm = new MainViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
            Avalonia.Application.Current!.RequestedThemeVariant = theme;
            bool advanced = vm.T5.T5Settings.EnableAdvancedMode;
            try
            {
                // File → Settings, T5 tab, Autotune settings
                Alone(new AutotuneSettingsWindow { DataContext = new TxSettingsViewModel(vm).NewT5Autotune() }, "autotune-settings" + suffix);
                _ = Dialogs.Report(window, "Examination report", vm.T5.Examine());
                Alone(Owned(window), "examine-report" + suffix);
                Alone(T5ToolWindows.Compressor(window, vm.T5), "compressor-map" + suffix);
                Alone(T5ToolWindows.InjectionTiming(window, vm.T5), "injection-timing" + suffix);
                var tune = vm.T5.TuneMeUp()!;
                // Stage 4 and higher: the free tuning settings
                tune.Stage = 4;
                Alone(new TuneMeUpWindow { DataContext = tune }, "tune-me-up" + suffix);
                // as on connect, the file written after the ECU
                _ = vm.T5.AskSyncAsync(new DateTime(2026, 10, 12, 18, 42, 7), new DateTime(2026, 9, 28, 10, 15, 33));
                Alone(Owned(window), "data-sync" + suffix);

                vm.Settings.Adapter = "";
                vm.ConfigureRealtimePanelCommand.Execute(null);
                var rt = (T5RealtimeViewModel)vm.Realtime!;
                Assert.AreEqual((int)T5RealtimeTab.Fuel, rt.SelectedTab);
                Maximized(window, "realtime-panel" + suffix);

                // Settings → T5 → Advanced mode enabled: the injector wizard in the Tuning menu, the autotune tabs
                vm.T5.T5Settings.EnableAdvancedMode = true;
                vm.T5.SettingsChanged();
                var injectors = vm.T5.InjectorWizard()!;
                injectors.Type = InjectorType.Siemens630Dekas;
                Alone(new InjectorWizardWindow { DataContext = injectors }, "injector-wizard" + suffix);
                // a fuel autotune under way (T5Suite's picture of it, no ECU here): a pull's wideband AFR in the feedback grid, Insp_mat! as
                // the multipliers it tunes; the session's own field set as AfrMapsTest does
                rt.Apply(new RealtimeSample(DateTime.Now, [("Rpm", 3540), ("P_medel", 0.75), ("Kyl_temp", 88), ("Lufttemp", 24)], 20, null));
                var afr = vm.T5.AfrMaps!;
                foreach (var (rpm, boost, a) in new[] { (1640, -0.45, 14.6), (2020, -0.3, 14.5), (2400, 0.15, 13.1), (2780, 0.45, 12.6), (3160, 0.6, 12.3),
                    (3540, 0.75, 12.1), (3920, 0.9, 11.9), (4300, 0.9, 12.0), (4680, 0.85, 12.2), (5060, 0.8, 12.3) })
                    afr.LogWidebandAFR(a, rpm, boost, false);
                var bin = (T5Binary)vm.Binary!;
                afr.SetCurrentFuelMap(bin.ReadSymbol(bin.Find("Insp_mat!")!));
                FieldInfo tuning = typeof(T5RealtimeViewModel).GetField("m_tuning", BindingFlags.NonPublic | BindingFlags.Instance)!;
                tuning.SetValue(rt, afr);
                typeof(T5RealtimeViewModel).GetMethod("ShowAutotuneGrids", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(rt, null);
                (rt.IsAutotuning, rt.AutotuneCaption) = (true, "Tuning...");
                rt.SelectedTab = (int)T5RealtimeTab.AutotuneFuel;
                Maximized(window, "realtime-autotune" + suffix);
                tuning.SetValue(rt, null);
                (rt.IsAutotuning, rt.AutotuneCaption) = (false, "Autotune fuel");
                // what the session proposes at its end: a handful of Insp_mat! cells along the pull (rows rpm, columns MAP)
                var percent = new double[256];
                foreach (var (row, col, p) in new[] { (6, 10, 2.5), (7, 10, 3.75), (7, 11, 4.25), (8, 11, 5.5), (8, 12, 3.0), (9, 12, 6.25), (9, 13, 2.75), (10, 13, -1.5) })
                    percent[row * 16 + col] = p;
                _ = vm.AcceptAutotune!(percent);
                Alone(Owned(window), "autotune-accept" + suffix);

                vm.T5.ShowAfrCommand.Execute("TargetAFR");
                Maximized(window, "afr-target" + suffix);
            }
            finally
            {
                vm.T5.T5Settings.EnableAdvancedMode = advanced;
                window.Close();
            }
        }

        // the dialog the window just opened (ShowDialog)
        private static Window Owned(Window owner)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            return owner.OwnedWindows.Last();
        }

        private static void Alone(Window w, string name)
        {
            if (!w.IsVisible) w.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            w.CaptureRenderedFrame();
            Save(w, name);
            w.Close();
        }
    }
}
