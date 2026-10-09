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
        public void QuickMapsFollowTheBin()
        {
            string dir = Directory.CreateTempSubdirectory("t7menu").FullName;
            try
            {
                string file = Path.Combine(dir, "5168646.bin");
                File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), file);
                var maps = T7Binary.Open(file, 0, false).QuickMaps();
                Assert.AreEqual("VE map", maps.First(m => m.Symbol == "BFuelCal.Map").Caption);
                Assert.IsFalse(maps.Any(m => m.Symbol == "IgnE85Cal.fi_AbsMap"));
                CollectionAssert.AreEqual(new[] { "Fuel", "Ignition", "Airmass request", "Boost control", "Knock", "Limiters" },
                    maps.Select(m => m.Group).Distinct().ToArray());

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
