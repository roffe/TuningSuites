using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using MapControls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using T7App.ViewModels;
using T7App.Views;

namespace T7AppTest
{
    public partial class AppTest
    {
        /// <summary>
        /// Dock measures inner windows at the workspace's size and arranges them at their own: the map viewer in a narrow one
        /// still fits its table and graph tabs to the window (MapControlsTest checks the mesh is centred in them).
        /// </summary>
        [TestMethod]
        public void MapViewerFitsANarrowInnerWindow()
        {
            string file = Path.Combine(s_dir, "narrow.bin");
            File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), file, true);
            s_session!.Dispatch(async () =>
            {
                var vm = new T7MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenPlainFileAsync(file, true));
                vm.OpenSymbolByName("IgnNormCal.Map");
                window.CaptureRenderedFrame();
                var dock = window.GetVisualDescendants().OfType<Dock.Avalonia.Controls.DocumentDockControl>().First().DataContext as Dock.Model.Controls.IDocumentDock;
                var mdi = (Dock.Model.Controls.IMdiDocument)dock!.VisibleDockables!.Single();
                var viewer = window.GetVisualDescendants().OfType<MapViewer>().Single();
                var tabs = viewer.GetVisualDescendants().OfType<TabControl>().First();

                foreach (var (w, h, name) in new[] { (500.0, 700.0, "mdi-narrow"), (1000, 800, "mdi-wide"), (460, 600, "mdi-narrower") })
                {
                    mdi.MdiBounds = new Dock.Model.Core.DockRect(0, 0, w, h);
                    Save(window, name);
                    double width = viewer.Bounds.Width;
                    Assert.IsTrue(width < w, name);
                    Assert.IsTrue(viewer.Grid.Bounds.Width <= width && tabs.Bounds.Width <= width && viewer.Surface.Bounds.Width <= width,
                        $"{name}: table {viewer.Grid.Bounds.Width}, graphs {tabs.Bounds.Width} in a viewer {width} wide");
                }
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
