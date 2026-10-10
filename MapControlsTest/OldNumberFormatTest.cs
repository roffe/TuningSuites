using System;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using MapControls;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MapControlsTest
{
    /// <summary>Numbers as the old suites showed them: .NET Framework's float rounding, the 3D graph per view, the user's culture.</summary>
    [TestClass]
    public class OldNumberFormatTest
    {
        private static void In(CultureInfo culture, Action a)
        {
            CultureInfo before = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = culture;
            try { a(); }
            finally { CultureInfo.CurrentCulture = before; }
        }

        // T5Suite's Insp_mat!: 8-bit, raw / 256 + 0.5
        private static MapData InspMat(byte raw) => new("Insp_mat!", [raw], 1, false) { Factor = 1.0 / 256, Offset = 0.5 };

        // float.ToString("F2") on .NET Framework rounded a half away from zero, .NET 10 rounds it to even
        [TestMethod]
        public void EasyHalvesRoundAwayFromZero()
        {
            In(CultureInfo.InvariantCulture, () =>
            {
                Assert.AreEqual("0.63", InspMat(32).FormatCell(0, MapViewType.Easy));                           // 0.625
                Assert.AreEqual("15%", new MapData("Reg_kon_mat_a!", [145], 1, false) { Factor = 0.1 }.FormatCell(0, MapViewType.Easy)); // 14.5
                Assert.AreEqual("-0.63", new MapData("Test.Map", [0xFF, 0xFB], 1, true) { Factor = 0.125 }.FormatCell(0, MapViewType.Easy)); // -0.625
                Assert.AreEqual("0.3", new MapData("Test.Map", [5], 1, false) { Factor = 0.05, EasyFormat = "F1" }.FormatCell(0, MapViewType.Easy)); // 0.25
                Assert.AreEqual("1.13", MapData.Fixed(1.125f, "F2"));
            });
        }

        // the editor opens on the shown "0.63", so Enter twice writes T5Suite's 0.63 (raw 33), not 0.62 (raw 31)
        [TestMethod]
        public void EditorStartsOnTheRoundedText()
        {
            Headless.Run(() => In(CultureInfo.InvariantCulture, () =>
            {
                MapData map = InspMat(32);
                var grid = new MapGrid { Map = map, ViewType = MapViewType.Easy };
                var window = new Window { Width = 200, Height = 100, Content = grid };
                window.Show();
                window.CaptureRenderedFrame();
                window.MouseDown(grid.CellCenter(0, 0), MouseButton.Left, RawInputModifiers.None);
                window.MouseUp(grid.CellCenter(0, 0), MouseButton.Left, RawInputModifiers.None);
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                Assert.AreEqual(33, map[0]);
                window.Close();
            }));
        }

        // MapViewerEx's FillData (all three suites): the 3D graph plots raw values outside the Easy view, refilled on a view change
        [TestMethod]
        public void SurfacePlotsTheTableNumbers()
        {
            Headless.Run(() =>
            {
                MapData map = MapGridTest.IgnitionMapData();
                var viewer = new MapViewer { Map = map };
                double Top() => viewer.Surface.ZMin + viewer.Surface.ZRange;
                Assert.AreEqual(map.PhysicalValues().Max(), Top(), 1e-9);
                Assert.AreEqual(1, viewer.Surface.ZPrec);

                viewer.ViewType = MapViewType.Decimal;
                Assert.AreEqual(map.RawValues().Max(), Top(), 1e-9);
                Assert.AreEqual(0, viewer.Surface.ZPrec);
                // an edit in the Decimal view plots raw too
                int top = Array.IndexOf(map.RawValues(), map.RawValues().Max());
                map.Set([(top, map[top] + 50)]);
                Assert.AreEqual(map.RawValues().Max(), Top(), 1e-9);

                viewer.ViewType = MapViewType.Hex;
                Assert.AreEqual(map.RawValues().Max(), Top(), 1e-9);
                viewer.ViewType = MapViewType.Easy;
                Assert.AreEqual(map.PhysicalValues().Max(), Top(), 1e-9);

                // T5Suite's 3-5 bar Decimal views: the scaled raw value; its Hex view shows the stored one
                viewer.ViewType = MapViewType.Decimal;
                viewer.Map = new MapData("Tryck_mat!", [100, 150], 2, false) { Factor = 0.01, Offset = -1, ScalePercent = 120 };
                Assert.AreEqual(180, Top(), 1e-9);
                viewer.ViewType = MapViewType.Hex;
                Assert.AreEqual(150, Top(), 1e-9);
            });
        }

        // the 3D labels in the user's number format, as the grid's headers
        [TestMethod]
        public void AxisLabelsInTheUsersCulture()
        {
            In(new CultureInfo("de-DE"), () =>
                CollectionAssert.AreEqual(new[] { "-0,76", "1,40" }, SurfaceAxes.Labels([-0.76, 1.4], 2, 2)));
        }

        // the log matrix's axes with two decimals in the Easy view's headers and in the graphs, the Decimal view's as they are
        [TestMethod]
        public void YAxisWithTwoDecimals()
        {
            Headless.Run(() => In(CultureInfo.InvariantCulture, () =>
            {
                var map = new MapData("Matrix", new byte[4], 2, false) { UpsideDown = true, YAxis = [0.5, 2], YEasy = [0.5, 2.004] };
                var viewer = new MapViewer { Map = map };
                Assert.AreEqual("2.00", viewer.Grid.YLabel(0));
                Assert.AreEqual("0.50", viewer.Grid.YLabel(1));
                CollectionAssert.AreEqual(map.YEasy, viewer.Surface.YData);
                Assert.AreEqual(2, viewer.Surface.YPrec);
                viewer.ViewType = MapViewType.Decimal;
                Assert.AreEqual("2", viewer.Grid.YLabel(0));
                Assert.AreEqual("0.5", viewer.Grid.YLabel(1));
            }));
        }
    }
}
