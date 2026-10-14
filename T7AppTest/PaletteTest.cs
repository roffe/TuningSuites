using System.IO;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TxSuite.ViewModels;
using TxSuite.Views;
using SuiteApp.ViewModels;
using T7App.ViewModels;
using T7App.Views;

namespace T7AppTest
{
    public partial class AppTest
    {
        /// <summary>Ctrl+P opens a symbol by part of its name, Ctrl+Shift+P runs any menu command, the run-time built menus too.</summary>
        [TestMethod]
        public void PalettesOpenSymbolsAndRunMenuCommands()
        {
            s_session!.Dispatch(async () =>
            {
                var vm = new MainViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(Path.Combine(s_dir, "5168646.bin"), true));

                window.KeyPress(Key.P, RawInputModifiers.Control, PhysicalKey.P, "p");
                Assert.IsNotNull(window.View.Palette);
                window.KeyTextInput("bfu");
                CollectionAssert.Contains(window.View.Palette.Shown.Select(e => e.Text).ToList(), "BFuelCal.Map");
                Assert.IsTrue(window.View.Palette.Shown.All(e => e.Text.Contains("bfu", System.StringComparison.OrdinalIgnoreCase)));
                SaveTopLevel(window.View.Palette, "palette-symbols");
                window.KeyTextInput("elcal.map");
                Assert.AreEqual("BFuelCal.Map", window.View.Palette.Shown[0].Text);
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                Dispatcher.UIThread.RunJobs();
                Assert.IsNull(window.View.Palette);
                Assert.AreEqual("BFuelCal.Map", ((MapViewerViewModel)vm.SelectedViewer!).MapName);

                // the Tuning menu is built when the bin opens; Escape runs nothing
                window.KeyPress(Key.P, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.P, "P");
                var commands = window.View.Palette!.Shown.Select(e => e.Text).ToList();
                CollectionAssert.Contains(commands, "Tuning > Fuel > VE map");
                CollectionAssert.Contains(commands, "File > Project > Create a project");
                // disabled items stay out (no project open)
                CollectionAssert.DoesNotContain(commands, "File > Project > Close project");
                int viewers = vm.Viewers.Count;
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                Dispatcher.UIThread.RunJobs();
                Assert.IsNull(window.View.Palette);
                Assert.AreEqual(viewers, vm.Viewers.Count);

                window.KeyPress(Key.P, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.P, "P");
                window.KeyTextInput("VIEW FILE IN");
                Assert.AreEqual("Actions > View file in hex", window.View.Palette!.Shown.Single().Text);
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                Dispatcher.UIThread.RunJobs();
                Assert.IsInstanceOfType<HexViewerViewModel>(vm.SelectedViewer);
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }

        // the palette's popup is its own top level
        private static void SaveTopLevel(Avalonia.Visual v, string name)
        {
            var frame = Avalonia.Controls.TopLevel.GetTopLevel(v)!.CaptureRenderedFrame()!;
            if (System.Environment.GetEnvironmentVariable("T7APP_DUMP") is { Length: > 0 } dir)
                frame.Save(Path.Combine(dir, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        }
    }
}
