using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SuiteCoreTest
{
    [TestClass]
    public class AirmassEstimateTest
    {
        // one fuel map cell (T8's 1/128) and an injector constant, nothing else
        private sealed class OneCell : AirmassResult
        {
            public OneCell(int ve, double injectorConstant) : base(new AirmassOptions())
            {
                VeOne = 128;
                FuelMap = [ve];
                FuelAir = [0];
                FuelRpm = [0];
                InjectorConst = injectorConstant;
            }

            public override bool CanE85 => false;
            public override bool CanVariant => false;
            public override bool CanOverboost => false;
            public override string[] LimiterMaps(AirmassLimitType type) => [];
        }

        /// <summary>
        /// CalculateTargetLambda (T7Suite, T8Suite) scaled 100 / VE by the duty cycle above 100 % and only then rounded to 0.01;
        /// CalculateFuelFlow returned whole l/h from that λ.
        /// </summary>
        [TestMethod]
        public void LambdaAndFuelFlowRoundedAsTheOldSuites()
        {
            // VE 163/128 at 115 % duty: 78.53 × 1.15 = 90.31, λ 0.90 (rounding first gave 79 × 1.15, 0.9085)
            var over = new OneCell(163, 227);
            Assert.AreEqual(115, over.InjectorDc(1000, 6000));
            Assert.AreEqual(0.90, over.TargetLambda(1000, 6000)!.Value, 1e-9);
            Assert.AreEqual(0.90 * 14.7, over.TargetAfr(1000, 6000)!.Value, 1e-9);
            // 1000 mg/c / 14650 / 0.90 × 200 combustions/s × 3600 / 742 = 73.6 l/h
            Assert.AreEqual(74, over.FuelFlow(1000, 6000));

            // below 100 % duty only the rounding
            var under = new OneCell(163, 1000);
            Assert.AreEqual(26, under.InjectorDc(1000, 6000));
            Assert.AreEqual(0.79, under.TargetLambda(1000, 6000)!.Value, 1e-9);
            Assert.AreEqual(84, under.FuelFlow(1000, 6000));
        }
    }
}
