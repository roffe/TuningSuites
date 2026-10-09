using System;
using System.Threading;
using System.Threading.Tasks;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SuiteCoreTest
{
    [TestClass]
    public class RealtimeEngineTest
    {
        // a Disconnect queued behind a pass: the loop still sees the session open, the next pass finds it closed
        private sealed class DisconnectingEngine : RealtimeEngine
        {
            public int Passes;

            protected override bool Connected => true;

            protected override Task<RealtimeSample> PassAsync(RealtimeSymbol[] rows, double fps) =>
                Task.FromResult(++Passes > 2 ? null : new RealtimeSample(DateTime.Now, [], fps, null));
        }

        [TestMethod]
        public void StopsQuietlyWhenTheSessionClosedBeforeAPass()
        {
            var engine = new DisconnectingEngine { Rows = [new RealtimeSymbol { Name = "ActualIn.n_Engine", Length = 2 }] };
            int samples = 0;
            engine.Sample += _ => samples++;
            Assert.IsTrue(engine.RunAsync(CancellationToken.None).Wait(TimeSpan.FromSeconds(5)));
            Assert.AreEqual(3, engine.Passes);
            Assert.AreEqual(2, samples);
        }
    }
}
