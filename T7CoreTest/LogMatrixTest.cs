using System;
using System.Collections.Generic;
using System.Linq;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using T7;

namespace T7CoreTest
{
    [TestClass]
    public class LogMatrixTest
    {
        [TestMethod]
        public void BinsHeldValuesIntoTheNearestCell()
        {
            var t = new DateTime(2026, 10, 9);
            var lines = new List<RealtimeLogLine>
            {
                new(t, [("rpm", 1000)]),                                    // no airmass / boost yet: not counted
                new(t, [("rpm", 1000), ("air", 100), ("boost", 0)]),
                new(t, [("rpm", 1000), ("air", 100), ("boost", 0.2)]),
                new(t, [("rpm", 6000), ("air", 1000), ("boost", 1.2)]),
                new(t, [("air", 1000), ("boost", 1.0)]),                  // rpm held at 6000
            };
            LogMatrix mean = LogMatrix.Build(lines, "rpm", "air", "boost", MatrixMode.Mean)!;
            Assert.AreEqual(1000, mean.X[0]);
            Assert.AreEqual(6000, mean.X[15]);
            Assert.AreEqual(0.1, mean.Values[0, 0], 1e-9);
            Assert.AreEqual(1.1, mean.Values[15, 15], 1e-9);
            Assert.IsTrue(double.IsNaN(mean.Values[5, 5]));
            Assert.AreEqual(0, LogMatrix.Build(lines, "rpm", "air", "boost", MatrixMode.Minimum)!.Values[0, 0]);   // a real 0 stays
            Assert.AreEqual(1.2, LogMatrix.Build(lines, "rpm", "air", "boost", MatrixMode.Maximum)!.Values[15, 15], 1e-9);
            Assert.IsNull(LogMatrix.Build(lines.Take(3), "rpm", "air", "boost", MatrixMode.Mean));                // rpm never changes
        }
    }
}
