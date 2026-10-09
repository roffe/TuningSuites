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
    }
}
