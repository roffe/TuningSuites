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

        private static double[] Rotation(M3 r) => [r.M00, r.M01, r.M02, r.M10, r.M11, r.M12, r.M20, r.M21, r.M22];

        // mean luminance of the pixels that differ from the background
        private static double MeshLuminance(Avalonia.Media.Imaging.WriteableBitmap frame, int width, int height)
        {
            uint bg = Headless.Pixel(frame, 2, 2);
            double sum = 0;
            int n = 0;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    uint p = Headless.Pixel(frame, x, y);
                    if (p == bg) continue;
                    // RGBA in memory
                    sum += 0.2126 * (p & 0xff) + 0.7152 * ((p >> 8) & 0xff) + 0.0722 * ((p >> 16) & 0xff);
                    n++;
                }
            Assert.IsGreaterThan(1000, n, "mesh pixels");
            return sum / n;
        }

        // brightest red or green channel of the solid surface: the pixels without blue, which the palette lacks and every
        // axis colour, both backgrounds and so the silhouette's blend with them have
        private static int MeshPeak(Avalonia.Media.Imaging.WriteableBitmap frame, int width, int height)
        {
            int peak = 0, n = 0;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    uint p = Headless.Pixel(frame, x, y);
                    if (((p >> 16) & 0xff) != 0) continue;
                    peak = Math.Max(peak, (int)Math.Max(p & 0xff, (p >> 8) & 0xff));
                    n++;
                }
            Assert.IsGreaterThan(1000, n, "surface pixels");
            return peak;
        }

        [TestMethod]
        public void ShaderLightsTheTopAndLeavesTheUndersideDark()
        {
            Headless.Run(() =>
            {
                var (v, x, y) = IgnitionMap();
                var views = new (string name, M3 rotation)[]
                {
                    ("above", M3.RotX(55) * M3.RotZ(-35)),
                    ("below", M3.RotX(125) * M3.RotZ(-35)),
                    ("oblique", M3.RotZ(-20) * M3.RotX(70) * M3.RotZ(40)),
                };
                // the headless canvas is a CPU one, which otherwise keeps the triangles
                Surface3D.ShadeOnCpu = true;
                try
                {
                    foreach (var theme in new[] { Avalonia.Styling.ThemeVariant.Dark, Avalonia.Styling.ThemeVariant.Light })
                    {
                        Avalonia.Application.Current!.RequestedThemeVariant = theme;
                        var lum = new double[views.Length];
                        for (int i = 0; i < views.Length; i++)
                        {
                            // solid only: the underside's grid lines are drawn brighter than its ambient fill
                            var surface = new Surface3D { RenderMode = SurfaceRenderMode.Solid };
                            surface.SetData(v, 18, 16, x, y, "mg/c", "rpm", "° BTDC", 0, 0, 1);
                            var rotation = views[i].rotation;
                            var frame = Headless.Render(surface, 640, 480, $"surface3d-shader-{views[i].name}-{theme}",
                                _ => surface.Camera = new Surface3D.CameraState(Rotation(rotation), 1, 0, 0));
                            Assert.IsTrue(surface.Shaded, "drawn by the SkSL shader");
                            lum[i] = MeshLuminance(frame, 640, 480);
                            // every palette colour has a full channel, so the unlit underside's ambient 0.32 caps it at 82
                            // (the means below miss it: the view-space light leaves most of the underside unlit anyway); lit
                            // by the diffuse term instead, some of its faces get twice that
                            if (i == 1)
                            {
                                int peak = MeshPeak(frame, 640, 480);
                                Assert.IsLessThanOrEqualTo(84, peak, $"below peak {peak} ({theme})");
                            }
                        }
                        string seen = $"above {lum[0]:F1}, below {lum[1]:F1}, oblique {lum[2]:F1} ({theme})";
                        Assert.IsLessThan(lum[0] * 0.75, lum[1], seen);
                        Assert.IsLessThan(lum[2] * 0.75, lum[1], seen);
                    }
                }
                finally
                {
                    Surface3D.ShadeOnCpu = false;
                    Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Default;
                }
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

                var row = new Surface3D();
                row.SetData([1, 3, 2, 5, 4], 5, 1, [1, 2, 3, 4, 5], null, "", "", "", 0, 0, 0);
                Headless.Render(row, 300, 200, "surface3d-row");
            });
        }
    }
}
