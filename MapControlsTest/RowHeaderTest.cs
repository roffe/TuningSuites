using System.Globalization;
using System.Linq;
using MapControls;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MapControlsTest
{
    [TestClass]
    public class RowHeaderTest
    {
        // MapViewerEx's row indicator (T5Suite, T7Suite, T8Suite): y[len - 1 - row] from the top, blank past the axis or without one
        [TestMethod]
        public void AxisFromTheTopRowBlankBeyond()
        {
            Headless.Run(() =>
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                // I_last_temp!: 16 rows, Last_temp_st! has 15 values (-40 … 80)
                double[] temps = [-40, -30, -20, -10, 0, 10, 20, 30, 40, 50, 60, 65, 70, 75, 80];
                var grid = new MapGrid { Map = new MapData("I_last_temp!", new byte[16], 1, false) { UpsideDown = true, YAxis = temps } };
                Assert.AreEqual("80 75 70 65 60 50 40 30 20 10 0 -10 -20 -30 -40 ", string.Join(" ", Enumerable.Range(0, 16).Select(grid.YLabel)));

                // Pwm_ind_trot!: 8 x 5, no Y axis
                grid.Map = new MapData("Pwm_ind_trot!", new byte[40], 8, false) { UpsideDown = true, YAxis = [] };
                Assert.IsTrue(Enumerable.Range(0, 5).All(r => grid.YLabel(r) == ""));

                // an axis as long as the map: the last value on top, as before
                MapData ign = MapGridTest.IgnitionMapData();
                ign.UpsideDown = true;
                grid.Map = ign;
                Assert.AreEqual(ign.YAxis![^1].ToString(CultureInfo.InvariantCulture), grid.YLabel(0));
                Assert.AreEqual(ign.YAxis[0].ToString(CultureInfo.InvariantCulture), grid.YLabel(ign.Rows - 1));
            });
        }

        // a Y axis longer than the map: the 3D graph labels the rows as the headers do (T7Suite's chart: y[len-1] on the top row down)
        [TestMethod]
        public void LongerAxisSameRowsIn3D()
        {
            Headless.Run(() =>
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                var viewer = new MapViewer { Map = new MapData("long", new byte[9], 3, false) { UpsideDown = true, YAxis = [10, 20, 30, 40, 50, 60] } };
                CollectionAssert.AreEqual(new double[] { 40, 50, 60 }, viewer.Surface.YData);
                Assert.AreEqual("60", viewer.Grid.YLabel(0));
                Assert.AreEqual("40", viewer.Grid.YLabel(2));
            });
        }
    }
}
