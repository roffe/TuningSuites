using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MapControls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;

namespace MapControlsTest
{
    [TestClass]
    public class ColorBlindTest
    {
        private static readonly ColorBlindMode[] Modes = Enum.GetValues<ColorBlindMode>();

        [TestMethod]
        public void ModesUseTxloggersStops()
        {
            // txlogger's pkg/colors/colorinterpolation.go: low, mid, high
            var stops = new (ColorBlindMode mode, Color low, Color mid, Color high)[]
            {
                (ColorBlindMode.Normal, Color.FromRgb(0, 255, 0), Color.FromRgb(255, 255, 0), Color.FromRgb(255, 0, 0)),
                (ColorBlindMode.Universal, Color.FromRgb(33, 102, 172), Color.FromRgb(247, 247, 247), Color.FromRgb(255, 165, 0)),
                (ColorBlindMode.Protanopia, Color.FromRgb(5, 113, 176), Color.FromRgb(247, 247, 247), Color.FromRgb(150, 75, 0)),
                (ColorBlindMode.Tritanopia, Color.FromRgb(0, 128, 128), Color.FromRgb(247, 247, 247), Color.FromRgb(215, 48, 39)),
                (ColorBlindMode.Deuteranomaly, Color.FromRgb(0x4A, 0x90, 0xE2), Color.FromRgb(0xF5, 0xE6, 0xB3), Color.FromRgb(0x8B, 0x45, 0x13)),
            };
            CollectionAssert.AreEqual(Modes, stops.Select(s => s.mode).ToArray(), "txlogger's order");
            foreach (var (mode, low, mid, high) in stops)
            {
                Assert.AreEqual(low, HeatColor.Interpolate(900, 1000, 900, false, mode), $"{mode} low");
                Assert.AreEqual(mid, HeatColor.Interpolate(900, 1000, 950, false, mode), $"{mode} mid");
                Assert.AreEqual(high, HeatColor.Interpolate(900, 1000, 1000, false, mode), $"{mode} high");
                Assert.AreEqual(Color.FromRgb(128, 128, 128), HeatColor.Interpolate(5, 5, 5, false, mode), $"{mode}: a flat map is grey");
                // T7Suite's online palette doesn't change
                Assert.AreEqual(HeatColor.Interpolate(0, 10, 3, true), HeatColor.Interpolate(0, 10, 3, true, mode));
            }
        }

        [TestMethod]
        public void TextTurnsWhiteOnlyOnDarkEnds()
        {
            for (int k = 0; k < 256; k++)
                Assert.AreSame(Brushes.Black, HeatColor.TextOn(HeatColor.Interpolate(0, 255, k)), $"green → red keeps black text (k {k})");
            Assert.AreSame(Brushes.White, HeatColor.TextOn(HeatColor.Interpolate(0, 1, 0, false, ColorBlindMode.Universal)));
            Assert.AreSame(Brushes.White, HeatColor.TextOn(HeatColor.Interpolate(0, 1, 1, false, ColorBlindMode.Deuteranomaly)));
            Assert.AreSame(Brushes.Black, HeatColor.TextOn(HeatColor.Interpolate(0, 1, 0.5, false, ColorBlindMode.Protanopia)));
        }

        [TestMethod]
        public void ParsesTxloggersNames()
        {
            Assert.AreEqual(ColorBlindMode.Protanopia, HeatColor.ParseMode("Protanopia"));
            Assert.AreEqual(ColorBlindMode.Tritanopia, HeatColor.ParseMode("tritanopia"));
            foreach (string? bad in new[] { null, "", "Unknown", "7" })
                Assert.AreEqual(ColorBlindMode.Normal, HeatColor.ParseMode(bad), bad ?? "null");
        }

        [TestMethod]
        public void ShaderPaletteIsHeatColorInEveryMode()
        {
            foreach (ColorBlindMode mode in Modes)
            {
                using var palette = SKBitmap.FromImage(SurfaceShader.ColormapImage(false, false, mode));
                for (int k = 0; k < 256; k++)
                {
                    Color c = HeatColor.Interpolate(0, 255, k, false, mode);
                    Assert.AreEqual(new SKColor(c.R, c.G, c.B), palette.GetPixel(k, 0), $"{mode} texel {k}");
                }
                Assert.AreSame(SurfaceShader.ColormapImage(false, true), SurfaceShader.ColormapImage(false, true, mode), "flat stays grey");
                Assert.AreSame(SurfaceShader.ColormapImage(true, false), SurfaceShader.ColormapImage(true, false, mode), "online stays T7Suite's");
            }
        }

        // pixels clearly green (the Normal scale's low end) and clearly blue (the colour blind scales' low ends)
        private static (int green, int blue) Hues(WriteableBitmap bmp)
        {
            int green = 0, blue = 0;
            for (int y = 0; y < bmp.PixelSize.Height; y++)
                for (int x = 0; x < bmp.PixelSize.Width; x++)
                {
                    uint p = Headless.Pixel(bmp, x, y);
                    int r = (int)(p & 0xff), g = (int)(p >> 8 & 0xff), b = (int)(p >> 16 & 0xff);
                    // bright and saturated: text edges on orange cells come out dark olive (47, 93, 5), and the 3D rpm axis, green in
                    // every mode, is (20, 130, 20) with pale anti-aliased labels (100, 172, 100)
                    if (g > 150 && r < 60 && b < 60) green++;
                    if (b > r + 40 && b > g + 20) blue++;
                }
            return (green, blue);
        }

        [TestMethod]
        public void ShownViewsRepaintInTheNewMode()
        {
            Headless.Run(() =>
            {
                var (v, x, y) = Surface3DTest.IgnitionMap();
                Control[] Views()
                {
                    var grid = new MapGrid { Map = MapGridTest.IgnitionMapData() };
                    var graph = new Graph2D();
                    graph.SetData(v.Take(18).ToArray(), x, v.Min(), v.Max(), 0, 1, "mg/c");
                    var surface = new Surface3D { RenderMode = SurfaceRenderMode.Solid };
                    surface.SetData(v, 18, 16, x, y, "mg/c", "rpm", "° BTDC", 0, 0, 1);
                    return [grid, graph, surface];
                }
                foreach (bool shader in new[] { false, true })
                {
                    Surface3D.ShadeOnCpu = shader;
                    try
                    {
                        foreach (Control view in Views())
                        {
                            string name = $"colorblind-{view.GetType().Name}{(view is Surface3D ? shader ? "-shader" : "-triangles" : "")}";
                            if (view is not Surface3D && shader) continue;
                            WriteableBitmap? before = null;
                            var after = Headless.Render(view, 640, 420, name + "-Universal", window =>
                            {
                                before = Headless.Capture(window, name + "-Normal");
                                view.SetValue(MapGrid.ColorModeProperty, ColorBlindMode.Universal);
                            });
                            var (green, blue) = Hues(before!);
                            Assert.IsGreaterThan(200, green, $"{name}: Normal is green at the low end");
                            (green, blue) = Hues(after);
                            Assert.AreEqual(0, green, $"{name}: Universal has no green");
                            Assert.IsGreaterThan(200, blue, $"{name}: Universal is blue at the low end");
                        }
                    }
                    finally
                    {
                        Surface3D.ShadeOnCpu = false;
                    }
                }
            });
        }

        [TestMethod]
        public void EveryModeRenders()
        {
            Headless.Run(() =>
            {
                var (v, x, y) = Surface3DTest.IgnitionMap();
                Surface3D.ShadeOnCpu = true;
                try
                {
                    foreach (ColorBlindMode mode in Modes)
                    {
                        var surface = new Surface3D { ColorMode = mode };
                        surface.SetData(v, 18, 16, x, y, "mg/c", "rpm", "° BTDC", 0, 0, 1);
                        Headless.Render(surface, 640, 480, $"colorblind-surface-{mode}");
                        Assert.IsTrue(surface.Shaded);
                        Headless.Render(new MapGrid { Map = MapGridTest.IgnitionMapData(), ColorMode = mode, OpenLoopMark = OpenLoopMark.Corner }, 900, 420, $"colorblind-grid-{mode}");
                    }
                }
                finally
                {
                    Surface3D.ShadeOnCpu = false;
                }
            });
        }
    }
}
