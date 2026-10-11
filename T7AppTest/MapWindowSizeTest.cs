using System.IO;
using System.Linq;
using Avalonia;
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
        /// A map's window opens at the size its table needs, at least 400 wide, the table at its natural height over the graph's eight
        /// rows; a large map at most at the working size. Every map opened at 60 % × 80 % of the workspace, a single value too (#4, #5).
        /// </summary>
        [TestMethod]
        public void MapWindowsOpenAtTheirTablesSize()
        {
            s_session!.Dispatch(async () =>
            {
                var vm = new T7MainWindowViewModel();
                var window = new MainWindow { DataContext = vm, Width = 1500, Height = 950 };
                window.Show();
                Assert.IsTrue(await vm.OpenFileAsync(Path.Combine(s_dir, "5168646.bin"), true));
                window.CaptureRenderedFrame();
                (Size window, Size table, Size natural) Open(string name)
                {
                    var v = vm.OpenSymbolByName(name)!;
                    for (int i = 0; i < 3; i++) window.CaptureRenderedFrame();
                    MapViewer viewer = window.GetVisualDescendants().OfType<MapViewer>().First(m => m.Map == v.Map);
                    var natural = new MapGrid { Map = v.Map, ViewType = v.ViewType, FontFamily = viewer.Grid.FontFamily, FontSize = viewer.Grid.FontSize };
                    natural.Measure(Size.Infinity);
                    Control inner = viewer.GetVisualAncestors().OfType<Control>().First(c => c.GetType().Name == "MdiDocumentWindow");
                    return (inner.Bounds.Size, viewer.Grid.Bounds.Size, natural.DesiredSize);
                }

                var one = Open("PwmLimitCal.Size");
                Assert.AreEqual(400, one.window.Width, 1);
                Assert.IsLessThan(450, one.window.Height);
                Assert.AreEqual(one.natural.Height, one.table.Height, 1);

                var map = Open("IgnNormCal.Map");
                // no wider than the table needs (narrower when the symbol list leaves less room), as high as other documents open
                Assert.IsLessThanOrEqualTo(map.natural.Width + 25, map.window.Width);
                Assert.AreEqual(DockHost(window).Bounds.Height * 0.8, map.window.Height, 1);
                window.Close();
                return true;
            }, default).GetAwaiter().GetResult();
        }
    }
}
