using System.Globalization;
using MapControls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SuiteApp;

namespace T7AppTest
{
    [TestClass]
    public class HyphenMinusTest
    {
        // ICU's Swedish minus is U+2212; the old suites showed "-0,80" for Tryck_mat! raw 20, and so do the apps after their start
        [TestMethod]
        public void NegativeNumbersWithAHyphen()
        {
            CultureInfo current = CultureInfo.CurrentCulture;
            CultureInfo? threads = CultureInfo.DefaultThreadCurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
                SuiteStartup.UseHyphenMinus();
                var tryck = new MapData("Tryck_mat!", [20], 1, false) { Factor = 0.01, Offset = -1 };
                Assert.AreEqual("-0,80", tryck.FormatCell(0, MapViewType.Easy));
                Assert.AreEqual("-40", (-40).ToString(CultureInfo.CurrentCulture));
                Assert.AreEqual("-", CultureInfo.DefaultThreadCurrentCulture!.NumberFormat.NegativeSign);
            }
            finally
            {
                CultureInfo.CurrentCulture = current;
                CultureInfo.DefaultThreadCurrentCulture = threads;
            }
        }
    }
}
