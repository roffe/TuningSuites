using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using MapControls;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MapControlsTest
{
    [TestClass]
    public class MapViewerTest
    {
        [TestMethod]
        public void ViewersOfTheSameMapFollowEachOther()
        {
            Headless.Run(() =>
            {
                MapData a = MapGridTest.IgnitionMapData(), b = MapGridTest.IgnitionMapData(), other = MapGridTest.IgnitionMapData();
                var va = new MapViewer { Map = a, SyncGroup = "IgnNormCal.Map" };
                var vb = new MapViewer { Map = b, SyncGroup = "IgnNormCal.Map" };
                var vc = new MapViewer { Map = other, SyncGroup = "BFuelCal.Map" };
                var window = new Window { Width = 1200, Height = 900, Content = new StackPanel { Children = { va, vb, vc } } };
                window.Show();

                va.Grid.Select([3, 4, 5]);
                CollectionAssert.AreEquivalent(new[] { 3, 4, 5 }, vb.Grid.SelectedCells.ToArray());
                Assert.IsEmpty(vc.Grid.SelectedCells);
                window.Close();
            });
        }

        private sealed class Nop : System.Windows.Input.ICommand
        {
            public event System.EventHandler? CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object? parameter) => true;
            public void Execute(object? parameter) { }
        }

        private static readonly Nop s_nop = new();

        /// <summary>Like Dock's MdiLayoutPanel: measures every inner window at the whole panel's size, arranges it at its own bounds.</summary>
        private sealed class MdiLikePanel : Panel
        {
            private Rect m_inner;

            public Rect Inner
            {
                get => m_inner;
                set { m_inner = value; InvalidateArrange(); }
            }

            protected override Size MeasureOverride(Size availableSize)
            {
                foreach (Control c in Children) c.Measure(availableSize);
                return availableSize;
            }

            protected override Size ArrangeOverride(Size finalSize)
            {
                foreach (Control c in Children) c.Arrange(m_inner);
                return finalSize;
            }
        }

        // everything inside the viewer, the mesh centred in the surface with its scales on screen
        private static void AssertFits(Window window, MapViewer viewer, string name)
        {
            Headless.Capture(window, name);
            double w = viewer.Bounds.Width;
            foreach (Control c in viewer.GetVisualDescendants().OfType<Control>()
                .Where(c => c is MapGrid or Surface3D or Graph2D or Slider or Button or ComboBox or TextBox or TabControl && c.IsEffectivelyVisible))
            {
                Point at = c.TranslatePoint(default, viewer)!.Value;
                Assert.IsTrue(at.X >= 0 && at.X + c.Bounds.Width <= w + 0.5, $"{name}: {c.GetType().Name} at {at.X}+{c.Bounds.Width} in a viewer {w} wide");
            }
            if (!viewer.Surface.IsAttachedToVisualTree()) return;
            Size surface = viewer.Surface.Bounds.Size;
            Rect drawn = viewer.Surface.DrawnBounds();
            Assert.AreEqual(surface.Width / 2, drawn.Center.X, 2, name);
            Assert.AreEqual(surface.Height / 2, drawn.Center.Y, 2, name);
            Assert.IsTrue(drawn.Left >= 0 && drawn.Right <= surface.Width && drawn.Top >= 0 && drawn.Bottom <= surface.Height,
                $"{name}: drawn {drawn} in {surface}");
        }

        [TestMethod]
        public void GraphsFollowTheTheme()
        {
            Headless.Run(() =>
            {
                var viewer = new MapViewer { Map = MapGridTest.IgnitionMapData() };
                var window = new Window { Width = 900, Height = 800, Content = viewer };
                window.Show();
                var graphTabs = viewer.GetVisualDescendants().OfType<TabControl>().First();
                // the surface's top left corner, nothing but background there
                uint Corner(Avalonia.Media.Imaging.WriteableBitmap frame)
                {
                    Point p = viewer.Surface.TranslatePoint(new Point(3, 3), window)!.Value;
                    return Headless.Pixel(frame, (int)p.X, (int)p.Y) & 0xFFFFFF;
                }
                try
                {
                    Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
                    Assert.AreEqual(0x202020u, Corner(Headless.Capture(window, "mapviewer-dark")));
                    graphTabs.SelectedIndex = 1;
                    Headless.Capture(window, "mapviewer-dark-2d");
                    graphTabs.SelectedIndex = 0;
                    // switching while shown repaints
                    Application.Current.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
                    Assert.AreEqual(0xFFFFFFu, Corner(Headless.Capture(window, "mapviewer-light")));
                    graphTabs.SelectedIndex = 1;
                    Headless.Capture(window, "mapviewer-light-2d");
                }
                finally
                {
                    Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Default;
                    window.Close();
                }
            });
        }

        [TestMethod]
        public void SurfaceStaysCentredInANarrowViewer()
        {
            Headless.Run(() =>
            {
                // an 18-column table is ~870 px wide at its natural size, the viewer is much narrower
                var viewer = new MapViewer { Map = MapGridTest.IgnitionMapData(), ReadEcuCommand = s_nop, WriteEcuCommand = s_nop };
                var window = new Window { Width = 500, Height = 700, Content = viewer };
                window.Show();
                AssertFits(window, viewer, "mapviewer-narrow");
                window.Width = 1100;
                AssertFits(window, viewer, "mapviewer-wide");
                window.Width = 420;
                AssertFits(window, viewer, "mapviewer-narrower");
                window.Close();

                // an MDI inner window: measured at the workspace's size, arranged at its own (the graph tabs used to be laid
                // out at the table's natural width, the mesh off centre and its scales clipped)
                viewer = new MapViewer { Map = MapGridTest.IgnitionMapData(), ReadEcuCommand = s_nop, WriteEcuCommand = s_nop };
                var mdi = new MdiLikePanel { Inner = new Rect(0, 0, 500, 700), Children = { viewer } };
                window = new Window { Width = 1200, Height = 900, Content = mdi };
                window.Show();
                AssertFits(window, viewer, "mapviewer-mdi-narrow");
                mdi.Inner = new Rect(20, 20, 1100, 800);
                AssertFits(window, viewer, "mapviewer-mdi-wide");
                mdi.Inner = new Rect(0, 0, 420, 600);
                AssertFits(window, viewer, "mapviewer-mdi-narrower");

                // the user's zoom survives a resize
                window.MouseWheel(viewer.Surface.TranslatePoint(new Point(50, 50), window)!.Value, new Vector(0, -1));
                double zoom = viewer.Surface.Camera.Zoom;
                Assert.AreNotEqual(1, zoom, 1e-3);
                mdi.Inner = new Rect(0, 0, 520, 640);
                Headless.Capture(window, "mapviewer-mdi-zoomed");
                Assert.AreEqual(zoom, viewer.Surface.Camera.Zoom, 1e-6);

                var graphTabs = viewer.GetVisualDescendants().OfType<TabControl>().First();
                graphTabs.SelectedIndex = 1;
                AssertFits(window, viewer, "mapviewer-mdi-2d");
                Assert.IsTrue(viewer.Graph.Bounds.Width > 0 && viewer.Graph.Bounds.Width <= viewer.Bounds.Width);
                window.Close();
            });
        }
    }
}
