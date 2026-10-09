using System.Linq;
using Avalonia.Controls;
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
    }
}
