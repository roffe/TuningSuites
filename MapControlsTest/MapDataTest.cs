using System.Globalization;
using System.Linq;
using MapControls;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MapControlsTest
{
    [TestClass]
    public class MapDataTest
    {
        [TestInitialize]
        public void Culture() => CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        // 3 columns x 2 rows, 16-bit: data row 0 = 1 2 0xFFFF(-1), row 1 = 0xF000 0xF001(-4095) 10
        private static MapData Map16() =>
            new("Test.Map", [0, 1, 0, 2, 0xFF, 0xFF, 0xF0, 0x00, 0xF0, 0x01, 0, 10], 3, true) { Factor = 0.1 };

        [TestMethod]
        public void DecodesLikeMapViewerEx()
        {
            MapData m = Map16();
            Assert.AreEqual(2, m.Rows);
            CollectionAssert.AreEqual(new[] { 1, 2, -1, 0xF000, -4095, 10 }, m.RawValues());
            CollectionAssert.AreEqual(new byte[] { 0, 1, 0, 2, 0xFF, 0xFF, 0xF0, 0x00, 0xF0, 0x01, 0, 10 }, m.ToBytes());

            var m8 = new MapData("B", [0, 200, 255], 3, false);
            CollectionAssert.AreEqual(new[] { 0, 200, 255 }, m8.RawValues());
        }

        [TestMethod]
        public void UpsideDownPutsTheLastDataRowOnTop()
        {
            MapData m = Map16();
            Assert.AreEqual(3, m.Index(0, 0));   // display row 0 = data row 1
            Assert.AreEqual((1, 2), m.Cell(2));
            m.UpsideDown = false;
            Assert.AreEqual(0, m.Index(0, 0));
        }

        [TestMethod]
        public void PartialLastRow()
        {
            var m = new MapData("P", [1, 2, 3, 4, 5], 2, false);
            Assert.AreEqual(3, m.Rows);
            Assert.AreEqual(-1, m.Index(0, 1)); // data row 2 has one cell, shown on top
            Assert.AreEqual(4, m.Index(0, 0));
        }

        [TestMethod]
        public void FormatsEachView()
        {
            MapData m = Map16();
            Assert.AreEqual("FFFF", m.FormatCell(2, MapViewType.Hex));
            Assert.AreEqual("-1", m.FormatCell(2, MapViewType.Decimal));
            Assert.AreEqual("-0.10", m.FormatCell(2, MapViewType.Easy));
            Assert.AreEqual("0.20", m.FormatCell(1, MapViewType.Easy));
            m.Factor = 1;
            Assert.AreEqual("2", m.FormatCell(1, MapViewType.Easy));
            Assert.AreEqual(" ", m.FormatCell(2, MapViewType.Ascii));
        }

        [TestMethod]
        public void ParsesAndRejects()
        {
            MapData m = Map16();
            Assert.IsTrue(m.TryParse("FFFE", MapViewType.Hex, out int raw, out _));
            Assert.AreEqual(-2, raw);
            Assert.IsTrue(m.TryParse("1.25", MapViewType.Easy, out raw, out _));
            Assert.AreEqual(12, raw); // 12.5 rounds half to even
            Assert.IsTrue(m.TryParse("1.35", MapViewType.Easy, out raw, out _));
            Assert.AreEqual(14, raw);
            Assert.IsFalse(m.TryParse("70000", MapViewType.Decimal, out _, out _));
            Assert.IsFalse(m.TryParse("A", MapViewType.Ascii, out _, out _));
            var m8 = new MapData("B", [0], 1, false);
            Assert.IsFalse(m8.TryParse("-1", MapViewType.Decimal, out _, out _));
            Assert.IsFalse(m8.TryParse("100", MapViewType.Hex, out _, out _));
        }

        [TestMethod]
        public void UndoRedoAndClamp()
        {
            MapData m = Map16();
            int changes = 0;
            m.Changed += (_, _) => changes++;
            m.Set([(0, 100), (1, 0x10000)]);
            CollectionAssert.AreEqual(new[] { 100, 0xF000 }, m.RawValues()[..2]);
            m.Set([(0, 5)]);
            m.Undo();
            Assert.AreEqual(100, m[0]);
            m.Undo();
            Assert.AreEqual(1, m[0]);
            Assert.IsFalse(m.CanUndo);
            m.Redo();
            Assert.AreEqual(100, m[0]);
            Assert.AreEqual(5, changes);
            Assert.IsTrue(m.Mutated);
        }

        [TestMethod]
        public void StepsAndMath()
        {
            var m = new MapData("B", [10, 250, 0], 3, false) { Factor = 0.5, Offset = 1 };
            int[] all = [0, 1, 2];
            MapOps.Step(m, all, MapViewType.Decimal, MapStep.PageUp);
            CollectionAssert.AreEqual(new[] { 20, 255, 10 }, m.RawValues());
            MapOps.Step(m, all, MapViewType.Hex, MapStep.PageDown);
            CollectionAssert.AreEqual(new[] { 4, 239, 0 }, m.RawValues());
            MapOps.Step(m, [2], MapViewType.Hex, MapStep.Max);
            Assert.AreEqual(255, m[2]);

            MapOps.Apply(m, all, MapViewType.Decimal, MapMath.Multiply, 2.5); // rounds to 2
            CollectionAssert.AreEqual(new[] { 8, 255, 255 }, m.RawValues());
            // Easy: physical 8*0.5+1 = 5, +2 = 7, back to raw (7-1)/0.5 = 12
            MapOps.Apply(m, [0], MapViewType.Easy, MapMath.Add, 2);
            Assert.AreEqual(12, m[0]);
            MapOps.Apply(m, [0], MapViewType.Easy, MapMath.Fill, 3.9); // (3.9-1)/0.5 = 5.8 truncates to 5
            Assert.AreEqual(5, m[0]);
            MapOps.Apply(m, [0], MapViewType.Decimal, MapMath.Divide, 0);
            Assert.AreEqual(5, m[0]);
        }

        [TestMethod]
        public void SmoothRowAndBlock()
        {
            var row = new MapData("R", [0, 9, 9, 9, 10], 5, false);
            Assert.IsTrue(MapOps.Smooth(row, [0, 1, 2, 3, 4]));
            CollectionAssert.AreEqual(new[] { 0, 2, 4, 6, 10 }, row.RawValues()); // integer step 10/4 = 2

            var block = new MapData("B", [0, 0, 0, 0, 100, 0, 0, 0, 0], 3, false);
            Assert.IsTrue(MapOps.Smooth(block, Enumerable.Range(0, 9).ToArray()));
            Assert.AreEqual(0, block[4]);
            Assert.IsFalse(MapOps.Smooth(block, [0, 1]));
        }

        [TestMethod]
        public void SelectByValueUsesPhysical()
        {
            MapData m = Map16();
            CollectionAssert.AreEqual(new[] { 0, 1 }, MapOps.SelectByValue(m, "0.1 0.2"));
        }

        [TestMethod]
        public void ClipboardRoundTripsInT7SuiteFormat()
        {
            MapData m = Map16();
            string text = MapOps.Copy(m, [0, 1], MapViewType.Easy);
            Assert.AreEqual("20:1:1:~1:1:2:~", text); // display row 1 = data row 0

            var target = new MapData("Test.Map", new byte[12], 3, true);
            Assert.IsTrue(MapOps.Paste(target, text));
            CollectionAssert.AreEqual(new[] { 1, 2, 0, 0, 0, 0 }, target.RawValues());

            var here = new MapData("Test.Map", new byte[12], 3, true);
            Assert.IsTrue(MapOps.Paste(here, text, (0, 1))); // anchor top row, column 1: second cell falls off
            CollectionAssert.AreEqual(new[] { 0, 0, 0, 0, 1, 2 }, here.RawValues());
            Assert.IsFalse(MapOps.Paste(here, "garbage"));
        }

        [TestMethod]
        public void OpenLoopComparesLimitWithXAxis()
        {
            MapData m = Map16();
            m.XAxis = [100, 200, 300];
            m.OpenLoop = [150, 250];
            Assert.IsTrue(m.IsOpenLoop(0));
            Assert.IsFalse(m.IsOpenLoop(1));
            Assert.IsTrue(m.IsOpenLoop(4));
        }
    }
}
