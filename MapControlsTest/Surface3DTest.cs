using System;
using System.Linq;
using Avalonia.Headless;
using MapControls;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MapControlsTest
{
    [TestClass]
    public class Surface3DTest
    {
        // ignition-like map: advance falls with load, rises with rpm
        internal static (double[] values, double[] x, double[] y) IgnitionMap(int cols = 18, int rows = 16)
        {
            double[] x = Enumerable.Range(0, cols).Select(i => 100.0 + i * 70).ToArray();     // mg/c
            double[] y = Enumerable.Range(0, rows).Select(i => 500.0 + i * 400).ToArray();    // rpm
            var v = new double[cols * rows];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    v[r * cols + c] = Math.Round(10 + 25.0 * r / rows - 18.0 * c / cols + 3 * Math.Sin(c / 2.0), 1);
            return (v, x, y);
        }

        [TestMethod]
        public void StaysCenteredWhenTheViewNarrows()
        {
            Headless.Run(() =>
            {
                var surface = new Surface3D();
                double[] values = Enumerable.Range(0, 18 * 16).Select(i => (double)(i % 18 + i / 18)).ToArray();
                surface.SetData(values, 18, 16, null, null, "x", "y", "z", 0, 0, 0);
                var window = new Avalonia.Controls.Window { Width = 900, Height = 400, Content = surface };
                window.Show();
                window.CaptureRenderedFrame();
                window.Width = 320;
                window.CaptureRenderedFrame();
                Avalonia.Rect drawn = surface.DrawnBounds();
                Assert.AreEqual(160, drawn.Center.X, 2);
                Assert.IsTrue(drawn.Left >= 0 && drawn.Right <= 320, drawn.ToString());

                // a synced camera from a much wider viewer of the same map: still centred and inside (it used to bring that
                // viewer's pixel zoom and pan along, off centre and clipped)
                var wide = new Surface3D();
                wide.SetData(values, 18, 16, null, null, "x", "y", "z", 0, 0, 0);
                var other = new Avalonia.Controls.Window { Width = 1400, Height = 900, Content = wide };
                other.Show();
                other.CaptureRenderedFrame();
                surface.Camera = wide.Camera;
                drawn = surface.DrawnBounds();
                Assert.AreEqual(160, drawn.Center.X, 2);
                Assert.IsTrue(drawn.Left >= 0 && drawn.Right <= 320, drawn.ToString());
                other.Close();
                window.Close();
            });
        }

        [TestMethod]
        public void RendersSurfaceAndAxes()
        {
            Headless.Run(() =>
            {
                var (v, x, y) = IgnitionMap();
                var surface = new Surface3D();
                surface.SetData(v, 18, 16, x, y, "mg/c", "rpm", "° BTDC", 0, 0, 1);
                surface.SetCursor(6.5, 4.2);
                var frame = Headless.Render(surface, 640, 480, "surface3d");

                // something other than the background in the middle of the view
                uint bg = Headless.Pixel(frame, 2, 2);
                Assert.AreNotEqual(bg, Headless.Pixel(frame, 320, 240));
            });
        }

        [TestMethod]
        public void FlatAndSingleCellMapsDoNotBreak()
        {
            Headless.Run(() =>
            {
                var flat = new Surface3D();
                flat.SetData(new double[12], 4, 3, null, null, "", "", "", 0, 0, 0);
                Headless.Render(flat, 300, 200, "surface3d-flat");

                var column = new Surface3D();
                column.SetData([1, 2, 3, 4, 5], 1, 5, null, [1, 2, 3, 4, 5], "", "rpm", "", 0, 0, 0);
                Headless.Render(column, 300, 200, "surface3d-column");
            });
        }
    }
}
