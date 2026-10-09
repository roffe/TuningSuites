using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using T7;

namespace T7CoreTest
{
    [TestClass]
    public class AirmassResultTest
    {
        private static string Here([CallerFilePath] string path = "") => Path.GetDirectoryName(path);

        [TestMethod]
        public void WotRowOnAStockBin()
        {
            T7Binary bin = T7Binary.Open(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), 0, false);
            Assert.IsTrue(T7AirmassResult.Available(bin));
            var r = new T7AirmassResult(bin, new AirmassOptions());
            int wot = r.Pedal.Length - 1, c2400 = System.Array.IndexOf(r.Rpm, 2400);
            // stock: the engine torque table limits WOT everywhere
            Assert.AreEqual(759, r.Airmass[wot, c2400]);
            Assert.AreEqual(AirmassLimitType.TorqueLimiterEngine, r.Limiter[wot, c2400]);
            Assert.AreEqual(245, r.Torque(759, 2400));
            Assert.AreEqual(82, AirmassResult.Power(245, 2400));
            Assert.AreEqual(38, r.InjectorDc(759, 2400));
            Assert.AreEqual(0.92, r.TargetLambda(759, 2400)!.Value, 1e-9);
            Assert.IsTrue(Enumerable.Range(0, r.Rpm.Length).All(c => r.Airmass[0, c] <= r.Airmass[wot, c]));

            // no firmware limit and a much higher torque table: the airmass limiter takes over somewhere
            var free = new T7AirmassResult(bin, new AirmassOptions { FirmwareLimited = false, Automatic = true });
            Assert.IsTrue(Enumerable.Range(0, r.Rpm.Length).All(c => free.Airmass[wot, c] > 0));

            // the interpolation: clamped at both ends, linear in between
            int[] axis = [0, 100], table = [10, 20, 30, 40];
            Assert.AreEqual(25, AirmassResult.Interpolate(table, axis, axis, 50, 50));
            Assert.AreEqual(10, AirmassResult.Interpolate(table, axis, axis, -5, -5));
            Assert.AreEqual(40, AirmassResult.Interpolate(table, axis, axis, 500, 500));
            Assert.IsNull(AirmassResult.Interpolate(null!, axis, axis, 0, 0));
        }
    }
}
