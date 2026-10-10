using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using T7;

namespace T7CoreTest
{
    [TestClass]
    public class MapMenusTest
    {
        private static string Here([CallerFilePath] string path = "") => Path.GetDirectoryName(path);

        [TestMethod]
        public void TuningMenuFollowsTheBin()
        {
            string dir = Directory.CreateTempSubdirectory("t7menu").FullName;
            try
            {
                string file = Path.Combine(dir, "5168646.bin");
                File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), file);
                var bin = T7Binary.Open(file, 0, false);
                string[] Maps(string menu, string caption) => bin.TuningMenu().Single(i => i.Caption == menu).Children.Single(i => i.Caption == caption).Maps(bin);
                CollectionAssert.AreEqual(new[] { "Actions", "Diagnostics", "Calibration", "Injectors", "Airmass", "Fuel", "Ignition", "Boost", "Knock", "Limiters", "Adaption", "Myrtilos" },
                    bin.TuningMenu().Select(i => i.Caption).ToArray());
                CollectionAssert.AreEqual(new[] { "BFuelCal.Map" }, Maps("Fuel", "VE map"));
                Assert.IsEmpty(Maps("Ignition", "Ignition for E85"));
                // EA1WF0: start ignition by air temperature only; a petrol bin's Common for tuning without the BioPower switch
                CollectionAssert.AreEqual(new[] { "IgnStartCal.fi_StartTab" }, Maps("Ignition", "Ignition Start"));
                CollectionAssert.AreEqual(new[] { "AdpFuelCal.T_AdaptLim", "FCutCal.ST_Enable", "LambdaCal.ST_Enable", "PurgeCal.ST_PurgeEnable", "TorqueCal.M_BrakeLimit" },
                    Maps("Fuel", "Common for tuning"));

                string xml = Path.Combine(dir, "mymaps.xml");
                MapMenus.AddToMyMaps(xml, "IgnNormCal.Map");
                MapMenus.SaveMyMaps(xml, MapMenus.LoadMyMaps(xml).Append(new MapShortcut("Boost", "Regulation", "BoostCal.RegMap")));
                var mine = MapMenus.LoadMyMaps(xml);
                Assert.HasCount(2, mine);
                Assert.AreEqual("Boost", mine[0].Group);       // categories sorted
                StringAssert.Contains(File.ReadAllText(xml), "<map title=\"Regulation\" symbol=\"BoostCal.RegMap\" />");
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}

namespace T7CoreTest
{
    [Microsoft.VisualStudio.TestTools.UnitTesting.TestClass]
    public class MapSearchTest
    {
        private static string Here([System.Runtime.CompilerServices.CallerFilePath] string path = "") => System.IO.Path.GetDirectoryName(path);

        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void FindsValuesNamesAndText()
        {
            string dir = System.IO.Directory.CreateTempSubdirectory("t7search").FullName;
            try
            {
                string file = System.IO.Path.Combine(dir, "5168646.bin");
                System.IO.File.Copy(System.IO.Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), file);
                var bin = T7.T7Binary.Open(file, 0, false);
                var map = bin.Find("IgnNormCal.Map");
                byte[] data = bin.ReadSymbol(map);
                // the last value of a 16-bit map: T7Suite's loop never got past the first half
                int last = data[^2] << 8 | data[^1];
                var byValue = MapSearch.Find(bin, new MapSearchOptions(true, (decimal)(last * 0.1f), false, "", false, false, true, map.Length));
                Microsoft.VisualStudio.TestTools.UnitTesting.CollectionAssert.Contains(byValue, map);
                var byName = MapSearch.Find(bin, new MapSearchOptions(false, 0, true, "IgnNormCal.Map", true, false, false, 0));
                Microsoft.VisualStudio.TestTools.UnitTesting.CollectionAssert.Contains(byName, map);
            }
            finally
            {
                System.IO.Directory.Delete(dir, true);
            }
        }
    }
}
