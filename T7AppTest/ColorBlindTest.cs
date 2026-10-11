using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using CommonSuite;
using MapControls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SuiteApp.ViewModels;
using T7App.ViewModels;
using T7App.Views;

namespace T7AppTest
{
    public partial class AppTest
    {
        [TestMethod]
        public void ColorBlindModeReachesOpenViewers()
        {
            string file = Path.Combine(s_dir, "colorblind.bin");
            File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), file, true);
            s_session!.Dispatch(async () =>
            {
                var vm = new T7MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                try
                {
                    Assert.IsTrue(await vm.OpenFileAsync(file, true));
                    SymbolHelper map = vm.Symbols!.Cast<SymbolHelper>().First(s => s.Varname == "IgnNormCal.Map");
                    vm.OpenSymbolCommand.Execute(map);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    var grid = window.GetVisualDescendants().OfType<MapGrid>().Single();
                    var surface = window.GetVisualDescendants().OfType<Surface3D>().Single();
                    Assert.AreEqual(ColorBlindMode.Normal, grid.ColorMode);
                    vm.ShowAirmassResultCommand.Execute(null);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    var airmass = window.GetVisualDescendants().OfType<SuiteApp.Controls.AirmassGrid>().Single();
                    Assert.IsGreaterThan(500, GreenIn(window, airmass), "the airmass table is green → red");

                    // the settings dialog's combo, saved under txlogger's name, and the viewer already open repaints
                    var settings = new SettingsViewModel(vm.Settings);
                    CollectionAssert.AreEqual(new[] { "Normal", "Universal", "Protanopia", "Tritanopia", "Deuteranomaly" }, settings.ColorBlindModes);
                    settings.ColorBlindMode = 2;
                    var settingsWindow = new SettingsWindow { DataContext = settings };
                    settingsWindow.Show();
                    Save(settingsWindow, "settings-colorblind");
                    settingsWindow.Close();
                    settings.Apply(vm.Settings);
                    vm.SettingsChanged();
                    Assert.AreEqual("Protanopia", new AppSettings(new T7.T7SuiteRegistry()).ColorBlindMode);
                    Assert.AreEqual(ColorBlindMode.Protanopia, grid.ColorMode);
                    Assert.AreEqual(ColorBlindMode.Protanopia, surface.ColorMode);
                    Assert.AreEqual(ColorBlindMode.Protanopia, airmass.ColorMode);
                    Assert.AreEqual(0, GreenIn(window, airmass), "and repaints blue → white → brown");
                    Save(window, "main-protanopia");
                    vm.SelectedViewer = vm.Viewers.OfType<MapViewerViewModel>().Single();
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    // the 2D graph joins the tree when its tab is chosen and takes the mode then
                    var viewer = window.GetVisualDescendants().OfType<MapViewer>().Single();
                    viewer.GetVisualDescendants().OfType<TabControl>().First().SelectedIndex = 1;
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    Assert.AreEqual(ColorBlindMode.Protanopia, viewer.Graph.ColorMode);

                    // and a viewer opened later starts in it
                    vm.OpenSymbolCommand.Execute(vm.Symbols!.Cast<SymbolHelper>().First(s => s.Varname == "BFuelCal.Map"));
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    Assert.IsTrue(window.GetVisualDescendants().OfType<MapGrid>().All(g => g.ColorMode == ColorBlindMode.Protanopia));
                }
                finally
                {
                    vm.Settings.ColorBlindMode = "Normal";
                    vm.SettingsChanged();
                    window.Close();
                }
                return true;
            }, default).GetAwaiter().GetResult();
        }

        // saturated green pixels (the green → red scale's low half) inside a control
        private static int GreenIn(Window window, Control control)
        {
            using var frame = window.CaptureRenderedFrame()!;
            Point origin = control.TranslatePoint(default, window)!.Value;
            int green = 0;
            using var pixels = frame.Lock();
            for (int y = (int)origin.Y; y < (int)(origin.Y + control.Bounds.Height); y++)
                for (int x = (int)origin.X; x < (int)(origin.X + control.Bounds.Width); x++)
                {
                    uint p = (uint)System.Runtime.InteropServices.Marshal.ReadInt32(pixels.Address, y * pixels.RowBytes + x * 4);
                    int r = (int)(p & 0xff), g = (int)(p >> 8 & 0xff), b = (int)(p >> 16 & 0xff);
                    if (g > 150 && r < 60 && b < 60) green++;
                }
            return green;
        }
    }
}
