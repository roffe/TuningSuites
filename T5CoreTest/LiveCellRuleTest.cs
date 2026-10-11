using System.Linq;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Trionic5Tools;

namespace T5CoreTest
{
    [TestClass]
    public class LiveCellRuleTest
    {
        // UpdateLiveView matched by caption: LOLA's Inj_map_0! is "TPS" x "RPM", so its column follows the throttle as read, not boost
        [TestMethod]
        public void InjMap0FollowsTps()
        {
            CellRule rule = T5Realtime.Rules.CellRules.First(r => r.Maps.Any("Inj_map_0!".StartsWith));
            Assert.AreEqual(CellInput.Tps, rule.X);
            Assert.AreEqual(1, rule.XScale);
            Assert.AreEqual(0, rule.XOffset);
            Assert.AreEqual(CellInput.Rpm, rule.Y);
        }
    }
}
