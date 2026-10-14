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
        public void OpenLoopBoxIsOnWholePixels()
        {
            Headless.Run(() =>
            {
                MapData map = IgnitionMapData();
                var grid = new MapGrid { Map = map, OpenLoopMark = OpenLoopMark.Box };
                // 641 px leaves fractional column widths, which used to blur every frame edge into grey
                var frame = Headless.Render(grid, 641, 420, "mapgrid-openloop-box");
                int open = Enumerable.Range(0, map.Rows * map.Cols).First(map.IsOpenLoop);
                var (r, c) = map.Cell(open);
                Avalonia.Rect cell = grid.CellRect(r, c);
                int x = (int)cell.Center.X, top = (int)Math.Round(cell.Y);
                Assert.AreEqual(0xFF000000u, Headless.Pixel(frame, x, top + 1), "frame, first pixel");
                Assert.AreEqual(0xFF000000u, Headless.Pixel(frame, x, top + 2), "frame, second pixel");
                Assert.AreEqual(Headless.Pixel(frame, x, top + 4), Headless.Pixel(frame, x, top + 3), "the cell colour right after the frame");
            });
        }

        /// <summary>
        /// A cell gap draws the cells as tiles with the background between them and no lines; the natural size grows by a gap per column
        /// and row. Without one the cells touch as before.
        /// </summary>
        [TestMethod]
        public void CellGapMakesTiles()
        {
            Headless.Run(() =>
            {
                const uint magenta = 0xFFFF00FF;
                MapData map = IgnitionMapData();
                var touching = new MapGrid { Map = map };
                var tiles = new MapGrid { Map = map, CellGap = 3 };
                touching.Measure(Avalonia.Size.Infinity);
                tiles.Measure(Avalonia.Size.Infinity);
                Assert.AreEqual(touching.DesiredSize.Width + map.Cols * 3, tiles.DesiredSize.Width, 0.001);
                Assert.AreEqual(touching.DesiredSize.Height + map.Rows * 3, tiles.DesiredSize.Height, 0.001);

                var frame = Headless.Render(new Border { Background = Avalonia.Media.Brushes.Magenta, Child = tiles }, 641, 420, "mapgrid-cellgap");
                Avalonia.Rect a = tiles.CellRect(2, 3), right = tiles.CellRect(2, 4), below = tiles.CellRect(3, 3);
                Assert.AreEqual(3, right.X - a.Right, 0.001);
                Assert.AreEqual(3, below.Y - a.Bottom, 0.001);
                int midY = (int)a.Center.Y, midX = (int)a.Center.X;
                Assert.AreEqual(magenta, Headless.Pixel(frame, (int)a.Right + 1, midY), "gap to the right");
                Assert.AreEqual(magenta, Headless.Pixel(frame, midX, (int)a.Bottom + 1), "gap below");
                Assert.AreEqual(magenta, Headless.Pixel(frame, (int)a.X - 2, midY), "gap to the left");
                Assert.AreNotEqual(magenta, Headless.Pixel(frame, (int)a.X, (int)a.Y), "the tile's first pixel");
                Assert.AreEqual(Headless.Pixel(frame, (int)a.X, (int)a.Y), Headless.Pixel(frame, (int)a.Right - 1, (int)a.Bottom - 1), "one fill to the tile's edges");

                frame = Headless.Render(new Border { Background = Avalonia.Media.Brushes.Magenta, Child = touching }, 641, 420, "mapgrid-nogap");
                Avalonia.Rect first = touching.CellRect(0, 0), last = touching.CellRect(map.Rows - 1, map.Cols - 1);
                for (int y = (int)first.Y; y < (int)last.Bottom; y += 3)
                    for (int x = (int)first.X; x < (int)last.Right; x += 3)
                        Assert.AreNotEqual(magenta, Headless.Pixel(frame, x, y), $"no background between touching cells ({x}, {y})");
            });
        }

        [TestMethod]
        public void T5SuiteMarksInsideTheBox()
        {
            Headless.Run(() =>
            {
                MapData map = IgnitionMapData();
                // T5Suite's marks on four cells of the top data row: knock adaption, boost adaption, 255 samples, a padlock
                int top = map.Rows - 1;
                map.Marks = i => (i - top * map.Cols) switch
                {
                    0 => new CellMarks(false, true, false, 0),
                    1 => new CellMarks(false, false, true, 0),
                    2 => new CellMarks(false, false, false, 255),
                    3 => new CellMarks(true, false, false, 0),
                    _ => default,
                };
                // every cell in the closed-loop box
                map.OpenLoop = [.. map.OpenLoop!.Select(_ => 1e6)];
                var grid = new MapGrid { Map = map, OpenLoopMark = OpenLoopMark.Box };
                var frame = Headless.Render(grid, 1100, 420, "mapgrid-t5marks");
                var asciiGrid = new MapGrid { Map = map, OpenLoopMark = OpenLoopMark.Box, ViewType = MapViewType.Ascii };
                var ascii = Headless.Render(asciiGrid, 1100, 420, "mapgrid-t5marks-ascii");
                uint Ring(MapGrid g, Avalonia.Media.Imaging.WriteableBitmap f, int col)
                {
                    Avalonia.Rect cell = g.CellRect(0, col);
                    return Headless.Pixel(f, (int)cell.Center.X, (int)Math.Round(cell.Y) + 3);
                }
                // one pixel inside the box's two (the frame is RGBA, as ColorBlindTest reads it)
                Assert.AreEqual(0xFFFF0000u, Ring(grid, frame, 0), "knock adaption blue");
                Assert.AreEqual(0xFFFFFFFFu, Ring(grid, frame, 1), "boost adaption white");
                Assert.AreEqual(0xFF00FF00u, Ring(grid, frame, 2), "255 samples green");
                Assert.AreNotEqual(0xFFFF0000u, Ring(grid, frame, 4), "no mark");
                Assert.AreNotEqual(0xFFFF0000u, Ring(asciiGrid, ascii, 0), "no marks in the ASCII view, as in T5Suite");
                // the padlock: its blue outline in the 10 × 10 at the right, the box's two pixels whole beside it (T5Suite drew it first)
                Avalonia.Rect locked = grid.CellRect(0, 3);
                int right = (int)Math.Round(locked.Right), middle = (int)locked.Center.Y;
                Assert.IsTrue(Enumerable.Range(right - 13, 10).Any(x => Enumerable.Range(middle - 5, 10)
                    .Any(y => Headless.Pixel(frame, x, y) is var p && (p >> 16 & 0xFF) > 0x90 && (p & 0xFF) < 0x40)), "padlock");
                Assert.IsTrue(Enumerable.Range(middle - 5, 10).All(y => Headless.Pixel(frame, right - 3, y) == 0xFF000000u
                    && Headless.Pixel(frame, right - 2, y) == 0xFF000000u), "the box beside the padlock");
            });
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
                Headless.Render(new MapGrid { Map = map, OnlineMode = true, OpenLoopMark = OpenLoopMark.Box }, 1100, 480, "mapgrid-online");
            });
        }
    }
}
