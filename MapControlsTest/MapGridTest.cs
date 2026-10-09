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
    [TestClass]
    public class MapGridTest
    {
        // the synthetic ignition map as T7Suite would load it: 16-bit raw in 0.1° steps
        internal static MapData IgnitionMapData()
        {
            var (v, x, y) = Surface3DTest.IgnitionMap();
            var bytes = new byte[v.Length * 2];
            for (int i = 0; i < v.Length; i++)
            {
                int raw = (int)Math.Round(v[i] * 10);
                bytes[i * 2] = (byte)(raw >> 8);
                bytes[i * 2 + 1] = (byte)raw;
            }
            return new MapData("IgnNormCal.Map", bytes, 18, true)
            {
                Factor = 0.1, XAxis = x, YAxis = y, XName = "mg/c", YName = "rpm", ZName = "° BTDC",
                OpenLoop = y.Select(r => 1300 - r / 10).ToArray(),
            };
        }

        [TestMethod]
        public void SelectTypeStepUndo()
        {
            Headless.Run(() =>
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                MapData map = IgnitionMapData();
                var grid = new MapGrid { Map = map, OpenLoopMark = OpenLoopMark.Corner };
                var window = new Window { Width = 1100, Height = 480, Content = grid };
                window.Show();
                window.CaptureRenderedFrame();

                // drag from display (row 1, col 2) to (row 2, col 4): 2x3 block
                window.MouseDown(grid.CellCenter(1, 2), MouseButton.Left, RawInputModifiers.None);
                window.MouseMove(grid.CellCenter(2, 4), RawInputModifiers.LeftMouseButton);
                window.MouseUp(grid.CellCenter(2, 4), MouseButton.Left, RawInputModifiers.None);
                Assert.HasCount(6, grid.SelectedCells);
                int[] cells = grid.SelectedCells.ToArray();

                // typing sets every selected cell (T7Suite's editor set only the focused one), as one undo step
                int focused = map.Index(2, 4);
                int[] before = map.RawValues();
                window.KeyTextInput("12.5");
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                Assert.IsTrue(cells.All(i => map[i] == 125));

                // steps apply to the whole selection; + and - don't open the editor
                window.KeyPress(Key.PageUp, RawInputModifiers.None, PhysicalKey.PageUp, null);
                Assert.IsTrue(cells.All(i => map[i] == 135));
                window.KeyPress(Key.Subtract, RawInputModifiers.None, PhysicalKey.NumPadSubtract, "-");
                window.KeyTextInput("-");
                Assert.AreEqual(134, map[focused]);
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); // opens the editor on the value
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                Assert.AreEqual(134, map[focused]);

                window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, null);
                window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, null);
                Assert.IsTrue(cells.All(i => map[i] == 125));
                window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, null);
                Assert.IsTrue(cells.All(i => map[i] == before[i]));

                // a focused cell outside the selection (ctrl-click toggled it off) is edited alone
                window.KeyPress(Key.Y, RawInputModifiers.Control, PhysicalKey.Y, null);
                window.MouseDown(grid.CellCenter(2, 4), MouseButton.Left, RawInputModifiers.Control);
                window.MouseUp(grid.CellCenter(2, 4), MouseButton.Left, RawInputModifiers.Control);
                window.KeyTextInput("20");
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                Assert.AreEqual(200, map[focused]);
                Assert.IsTrue(cells.Where(i => i != focused).All(i => map[i] == 125));

                grid.SetHighlight(6, 9);
                Headless.Capture(window, "mapgrid");
                window.Close();
            });
        }

        [TestMethod]
        public void RendersEachViewAndStyle()
        {
            Headless.Run(() =>
            {
                MapData map = IgnitionMapData();
                Headless.Render(new MapGrid { Map = map, ViewType = MapViewType.Hex }, 1100, 480, "mapgrid-hex");
                Headless.Render(new MapGrid { Map = map, IsRedWhite = true }, 1100, 480, "mapgrid-redwhite");
                Headless.Render(new MapGrid { Map = map, OnlineMode = true, OpenLoopMark = OpenLoopMark.Box }, 1100, 480, "mapgrid-online");
            });
        }
    }
}
