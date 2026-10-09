using MapControls;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MapControlsTest
{
    [TestClass]
    public class Graph2DTest
    {
        [TestMethod]
        public void NiceSteps()
        {
            Assert.AreEqual(1.0, Graph2D.NiceStep(8, 8));
            Assert.AreEqual(2.0, Graph2D.NiceStep(15, 8));
            Assert.AreEqual(5.0, Graph2D.NiceStep(33, 8));
            Assert.AreEqual(0.05, Graph2D.NiceStep(0.3, 8), 1e-12);
        }

        [TestMethod]
        public void RendersSlice()
        {
            Headless.Run(() =>
            {
                var (v, x, _) = Surface3DTest.IgnitionMap();
                var row = v[(5 * 18)..(6 * 18)];
                var graph = new Graph2D();
                graph.SetData(row, x, -4.6, 34, 0, 1, "mg/c");
                graph.SetCursor(6.5);
                Headless.Render(graph, 640, 300, "graph2d");
            });
        }
    }
}
