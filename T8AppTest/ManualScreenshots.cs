using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.VisualTree;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TxSuite.ViewModels;
using TxSuite.Views;
using T8App.Views;
using T8SuitePro;

namespace T8AppTest
{
    public partial class AppTest
    {
        /// <summary>The T8 manual's shots (t8-manual/*.qmd and the shared chapters' map preview, named as their screenshot markers), on a main window of their own.</summary>
        private static async Task ManualScreenshots(string file, ThemeVariant theme, string suffix)
        {
            var vm = new MainViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
            window.Show();
            Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
            Avalonia.Application.Current!.RequestedThemeVariant = theme;
            var bin = (T8Binary)vm.Binary!;

            // Settings → Common → Show map preview popup (on by default): the pointer resting on the name of the symbol picked in the list
            Assert.IsTrue(vm.Settings.ShowMapPreviewPopup);
            SymbolHelper map = bin.Find("IgnAbsCal.fi_NormalMAP");
            vm.SelectedSymbol = map;
            var grid = window.GetVisualDescendants().OfType<DataGrid>().First(g => g.Name == "SymbolGrid");
            grid.ScrollIntoView(map, null);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
            // its row mid-list, the popup beside it (ScrollIntoView leaves it at the top)
            window.MouseWheel(grid.TranslatePoint(new Point(100, 300), window)!.Value, new Vector(0, 5));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
            var name = window.GetVisualDescendants().OfType<Border>().First(b => b.DataContext == map && ToolTip.GetTip(b) != null);
            window.MouseMove(name.TranslatePoint(new Point(60, name.Bounds.Height / 2), window)!.Value);
            // past its ShowDelay
            for (int i = 0; i < 40 && !ToolTip.GetIsOpen(name); i++)
            {
                await Task.Delay(50);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            }
            Assert.IsTrue(ToolTip.GetIsOpen(name));
            window.CaptureRenderedFrame();
            Save(window, "map-preview" + suffix);
            window.MouseMove(new Point(1000, 600));
            ToolTip.SetIsOpen(name, false);

            Alone(new PidEditorWindow { DataContext = vm.T8.PidEditor(false) }, "pid-editor" + suffix);

            // a bit mask symbol in flash, opened from the symbol list; closed unchanged
            vm.OpenSymbolCommand.Execute(bin.Symbols.Cast<SymbolHelper>().First(s => s.BitMask > 0 && s.Flash_start_address < 0x100000));
            for (int i = 0; i < 100 && !window.OwnedWindows.OfType<BitmaskWindow>().Any(); i++) await Task.Delay(50);
            Alone(window.OwnedWindows.OfType<BitmaskWindow>().Single(), "bitmask" + suffix);

            // Tuning Wizard's second page: the Tuning Actions that fit the bin
            var wizard = vm.T8.TuningWizard()!;
            var dialog = new TuningWizardWindow { DataContext = wizard };
            dialog.Show();
            await wizard.NextAsync();
            Alone(dialog, "tuning-wizard" + suffix);

            // Get ECU information as an ECU with this bin answers (T8Ecu.ReadInfoAsync's rows); what only the ECU knows made up
            const string ecu = "ECU related data", cal = "Calibration data";
            FirmwareInfo info = vm.T8.FirmwareInfo()!;
            Alone(new EcuInfoWindow(
            [
                new(ecu, "ECU description", "P6.8", "Trionic 8"),
                new(ecu, "Hardware type", info.HardwareType, info.HardwareId),
                new(ecu, "Build date", info.ReleaseDate),
                new(ecu, "Serial number", info.SerialNumber),
                new(ecu, "SAAB partnumber", "55575467"),
                new(ecu, "Basemodel partnumber", info.PartNumber, "55560579"),
                new(cal, "Calibration set", "P_37_FIEF_"),
                new(cal, "Codefile version", "FA56"),
                new(cal, "Software version", info.SoftwareVersion),
                new(cal, "Software version file", "FA56_C_FME2_XX_XXX_XXX.tmp"),
                new(cal, "Software IDs", "55560572", "12799173"),
                new(cal, "", "55353555", "55352573"),
                new(cal, "", "55352574", "55352572"),
                new(cal, "VIN number", "YS3FB45F041000001"),
                new(cal, "Engine type", "81c"),
                new(cal, "Speedlimit", "230 km/h"),
            ]), "ecu-information" + suffix);
            window.Close();
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
