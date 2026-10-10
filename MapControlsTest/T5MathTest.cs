using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using MapControls;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MapControlsTest
{
    /// <summary>T5Suite's MapViewerEx math and keys: FractionalMath, and the 3.0 to 5.0 bar views (ScalePercent).</summary>
    [TestClass]
    public class T5MathTest
    {
        private static int Apply(MapData m, MapViewType view, MapMath op, double w)
        {
            MapOps.Apply(m, [0], view, op, w);
            return m[0];
        }

        private static MapData Cell(int raw, bool sixteenBit = false, double factor = 1, double offset = 0) =>
            new("T", sixteenBit ? [(byte)(raw >> 8), (byte)raw] : [(byte)raw], 1, sixteenBit)
            { Factor = factor, Offset = offset, FractionalMath = true };

        // a Tryck_mat! cell on a 3.0 bar file: raw 100 shows 120 / 0.20
        private static MapData Boost3Bar(int raw = 100) => new("Tryck_mat!", [(byte)raw], 1, false) { Factor = 0.01, Offset = -1, ScalePercent = 120, FractionalMath = true };

        [TestMethod]
        public void MultiplyAndDivideKeepTheFraction()
        {
            // Insp_mat! raw 25: v*(int)(100*w)/100 and (int)(v/w); T7Suite's rule rounds w and leaves it at 25
            Assert.AreEqual(27, Apply(Cell(25), MapViewType.Decimal, MapMath.Multiply, 1.1));
            Assert.AreEqual(37, Apply(Cell(25), MapViewType.Decimal, MapMath.Multiply, 1.5));
            Assert.AreEqual(22, Apply(Cell(25), MapViewType.Decimal, MapMath.Divide, 1.1));
            Assert.AreEqual(50, Apply(Cell(25), MapViewType.Decimal, MapMath.Divide, 0.5));
            Assert.AreEqual(105, Apply(Cell(100), MapViewType.Decimal, MapMath.Multiply, 1.05));
            Assert.AreEqual(220, Apply(Cell(200, true), MapViewType.Hex, MapMath.Multiply, 1.1));
            Assert.AreEqual(25, Apply(Cell(25), MapViewType.Decimal, MapMath.Divide, 0));
            MapData t7 = Cell(25);
            t7.FractionalMath = false;
            Assert.AreEqual(25, Apply(t7, MapViewType.Decimal, MapMath.Multiply, 1.1));
        }

        [TestMethod]
        public void EasyResultsRound()
        {
            // Ign_map_0! 6.0° + 0.3° = 6.3° (raw 63), truncated it was 6.2°
            Assert.AreEqual(63, Apply(Cell(60, true, 0.1), MapViewType.Easy, MapMath.Add, 0.3));
            // Tryck_mat! 0.00 - 0.07 = -0.07 (raw 93), truncated -0.08
            Assert.AreEqual(93, Apply(Cell(100, false, 0.01, -1), MapViewType.Easy, MapMath.Add, -0.07));
            // (3.9-1)/0.5 = 5.8 rounds to 6
            Assert.AreEqual(6, Apply(Cell(0, false, 0.5, 1), MapViewType.Easy, MapMath.Fill, 3.9));
        }

        [TestMethod]
        public void ScaledViewsStepAndCalculateOnTheShownValue()
        {
            MapData m = Boost3Bar();
            MapOps.Step(m, [0], MapViewType.Decimal, MapStep.PageUp);
            Assert.AreEqual(109, m[0]); // 120 + 10 = 130, back to raw rounded up
            Assert.AreEqual("130", m.FormatCell(0, MapViewType.Decimal));
            m = Boost3Bar();
            MapOps.Step(m, [0], MapViewType.Decimal, MapStep.Down);
            Assert.AreEqual(99, m[0]); // 119 would round back up to 100
            m = Boost3Bar();
            MapOps.Step(m, [0], MapViewType.Hex, MapStep.PageUp);
            Assert.AreEqual(116, m[0]); // hex stays raw

            Assert.AreEqual(109, Apply(Boost3Bar(), MapViewType.Easy, MapMath.Add, 0.10));      // 0.30
            Assert.AreEqual(102, Apply(Boost3Bar(), MapViewType.Easy, MapMath.Multiply, 1.10)); // 0.22
            Assert.AreEqual(125, Apply(Boost3Bar(), MapViewType.Easy, MapMath.Fill, 0.50));     // 0.50, as typed
            Assert.AreEqual(125, Apply(Boost3Bar(), MapViewType.Decimal, MapMath.Fill, 150));
        }

        [TestMethod]
        public void ScaledEasyEditorStartsOnTheShownValue()
        {
            Headless.Run(() =>
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                MapData map = Boost3Bar(140); // shows 0.68
                var grid = new MapGrid { Map = map, ViewType = MapViewType.Easy };
                var window = new Window { Width = 200, Height = 100, Content = grid };
                window.Show();
                window.CaptureRenderedFrame();
                window.MouseDown(grid.CellCenter(0, 0), MouseButton.Left, RawInputModifiers.None);
                window.MouseUp(grid.CellCenter(0, 0), MouseButton.Left, RawInputModifiers.None);
                // Enter opens the editor on "0.68", Enter again keeps the cell
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                Assert.AreEqual(140, map[0]);
                Assert.IsFalse(map.Mutated);
                window.Close();
            });
        }
    }
}
