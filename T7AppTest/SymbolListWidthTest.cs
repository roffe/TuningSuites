using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using T7App.ViewModels;
using T7App.Views;

namespace T7AppTest
{
    public partial class AppTest
    {
        private static double SymbolListWidth(MainWindow window) =>
            window.GetVisualDescendants().OfType<SymbolListView>().Single().Bounds.Width;

        /// <summary>The symbol list keeps the width it was dragged to in the next session.</summary>
        [TestMethod]
        public void SymbolListWidthIsRemembered()
        {
            s_session!.Dispatch(() =>
            {
                var window = new MainWindow { DataContext = new MainWindowViewModel(), Width = 1500, Height = 950 };
                window.Show();
                window.CaptureRenderedFrame();
                double before = SymbolListWidth(window);
                var splitter = window.GetVisualDescendants().OfType<Control>().First(c => c.GetType().Name == "ProportionalStackPanelSplitter");
                Point from = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), window)!.Value;
                window.MouseDown(from, MouseButton.Left, RawInputModifiers.None);
                window.MouseMove(from + new Point(200, 0), RawInputModifiers.LeftMouseButton);
                window.MouseUp(from + new Point(200, 0), MouseButton.Left, RawInputModifiers.None);
                window.CaptureRenderedFrame();
                double dragged = SymbolListWidth(window);
                Assert.IsGreaterThan(before + 150, dragged);
                window.Close();

                window = new MainWindow { DataContext = new MainWindowViewModel(), Width = 1500, Height = 950 };
                window.Show();
                window.CaptureRenderedFrame();
                Assert.AreEqual(dragged, SymbolListWidth(window), 2);
                window.Close();
                // the other tests start from the default width
                using (var settings = CommonSuite.SettingsKey.Open(MainWindowViewModel.Suite)) settings.DeleteValue("SymbolListProportion");
                return true;
            }, default).GetAwaiter().GetResult();
        }

        private static double DocumentsLeft(MainWindow window)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame();
            var area = window.GetVisualDescendants().OfType<Dock.Avalonia.Controls.DocumentDockControl>().First();
            return area.TranslatePoint(default, window)!.Value.X;
        }

        /// <summary>
        /// The symbol list pinned to the side gives its width to the inner windows: the empty pane kept it, an invisible wall
        /// the windows couldn't be moved past.
        /// </summary>
        [TestMethod]
        public void PinnedSymbolListLeavesTheWidthToTheWindows()
        {
            s_session!.Dispatch(() =>
            {
                var window = new MainWindow { DataContext = new MainWindowViewModel(), Width = 1500, Height = 950 };
                window.Show();
                double docked = DocumentsLeft(window);
                Assert.IsGreaterThan(300, docked);
                var factory = window.FindControl<Dock.Avalonia.Controls.DockControl>("Workspace")!.Factory!;
                var tool = Avalonia.Controls.NameScope.GetNameScope(window)!.Find<Dock.Model.Avalonia.Controls.Tool>("SymbolTool")!;
                factory.PinDockable(tool);
                Assert.IsLessThan(50, DocumentsLeft(window), "only the pinned tab strip left of the windows");
                factory.UnpinDockable(tool);
                Assert.AreEqual(docked, DocumentsLeft(window), 1);
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }

        /// <summary>Hide symbol window (T7Suite's auto hidden symbol list): pinned from the start, slid back in when a map opens.</summary>
        [TestMethod]
        public void HideSymbolWindowPinsTheSymbolList()
        {
            string file = System.IO.Path.Combine(s_dir, "hidden.bin");
            System.IO.File.Copy(System.IO.Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), file, true);
            s_session!.Dispatch(async () =>
            {
                var vm = new MainWindowViewModel();
                vm.Settings.HideSymbolTable = true;
                try
                {
                    var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                    window.Show();
                    Assert.IsLessThan(50, DocumentsLeft(window));
                    var factory = window.FindControl<Dock.Avalonia.Controls.DockControl>("Workspace")!.Factory!;
                    var tool = Avalonia.Controls.NameScope.GetNameScope(window)!.Find<Dock.Model.Avalonia.Controls.Tool>("SymbolTool")!;
                    Assert.IsTrue(factory.IsDockablePinned(tool));
                    Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                    factory.PreviewPinnedDockable(tool);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    Assert.IsTrue(window.GetVisualDescendants().OfType<SymbolListView>().Any(v => v.IsEffectivelyVisible), "slid out");
                    vm.OpenSymbolByName("IgnNormCal.Map");
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    Assert.IsFalse(window.GetVisualDescendants().OfType<SymbolListView>().Any(v => v.IsEffectivelyVisible), "slid back in");
                    window.Close();
                }
                finally
                {
                    vm.Settings.HideSymbolTable = false;
                }
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
