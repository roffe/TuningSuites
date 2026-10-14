using System.Globalization;
using System.IO;
using System.Linq;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SuiteCoreTest
{
    [TestClass]
    public class SettingsKeyTest
    {
        [TestMethod]
        public void RoundTripsLikeTheRegistryDid()
        {
            string dir = Directory.CreateTempSubdirectory("t7settings").FullName;
            string file = Path.Combine(dir, "sub", "settings.json");
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
                using (var key = new SettingsKey(file, null))
                {
                    Assert.IsEmpty(key.GetValueNames());
                    Assert.IsNull(key.GetValue("Missing"));
                    key.SetValue("Flag", true);
                    key.SetValue("Count", 42);
                    key.SetValue("Voltage", 1.5.ToString());
                }

                using (var key = new SettingsKey(file, null))
                {
                    Assert.HasCount(3, key.GetValueNames());
                    Assert.IsTrue(System.Convert.ToBoolean(key.GetValue("Flag").ToString()));
                    Assert.AreEqual(42, System.Convert.ToInt32(key.GetValue("Count").ToString()));
                    Assert.AreEqual("1,5", key.GetValue("Voltage"));
                }

                File.WriteAllText(file, "{ not json");
                using (var key = new SettingsKey(file, null))
                {
                    Assert.IsEmpty(key.GetValueNames());
                }
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [TestMethod]
        public void ARenamedSuiteTakesItsFormerFolder()
        {
            string old = SettingsKey.BaseFolder, dir = Directory.CreateTempSubdirectory("suiterename").FullName;
            try
            {
                SettingsKey.BaseFolder = dir;
                SettingsKey.FormerFolders["RenamedSuite"] = "FormerSuite";
                Directory.CreateDirectory(Path.Combine(dir, "FormerSuite"));
                File.WriteAllText(Path.Combine(dir, "FormerSuite", "settings.json"), """{ "Lastfilename": "a.bin" }""");
                File.WriteAllText(Path.Combine(dir, "FormerSuite", "UserLib.json"), "[]");

                using (var key = SettingsKey.Open("RenamedSuite"))
                    Assert.AreEqual("a.bin", key.GetValue("Lastfilename"));
                Assert.IsFalse(Directory.Exists(Path.Combine(dir, "FormerSuite")));
                Assert.IsTrue(File.Exists(Path.Combine(dir, "RenamedSuite", "UserLib.json")), "the whole folder moves");

                // once there, a folder under the former name is left alone
                Directory.CreateDirectory(Path.Combine(dir, "FormerSuite"));
                Assert.AreEqual(Path.Combine(dir, "RenamedSuite"), SettingsKey.Folder("RenamedSuite"));
                Assert.IsTrue(Directory.Exists(Path.Combine(dir, "FormerSuite")));
            }
            finally
            {
                SettingsKey.FormerFolders.Remove("RenamedSuite");
                SettingsKey.BaseFolder = old;
                Directory.Delete(dir, true);
            }
        }

        [TestMethod]
        public void SubkeysShareOneFileLikeLogFilters()
        {
            string dir = Directory.CreateTempSubdirectory("t7settings").FullName;
            string file = Path.Combine(dir, "settings.json");
            try
            {
                using (var key = new SettingsKey(file, null)) key.SetValue("Top", "1");
                using (var key = new SettingsKey(file, null, @"LogFilters\0")) key.SetValue("symbol", "In.n_Engine");
                using (var key = new SettingsKey(file, null, @"LogFilters\1")) key.SetValue("symbol", "Out.X_AccPos");
                using (var key = new SettingsKey(file, null, "Channels")) key.SetValue("RPM", true);

                using (var root = new SettingsKey(file, null))
                {
                    CollectionAssert.AreEquivalent(new[] { "Top" }, root.GetValueNames());
                    CollectionAssert.AreEquivalent(new[] { "LogFilters", "Channels" }, root.GetSubKeyNames());
                }
                using (var filters = new SettingsKey(file, null, "LogFilters"))
                {
                    Assert.IsEmpty(filters.GetValueNames());
                    CollectionAssert.AreEquivalent(new[] { "0", "1" }, filters.GetSubKeyNames());
                }
                // registry names are case-insensitive, Channels upper-cases them
                using (var channels = new SettingsKey(file, null, "channels"))
                {
                    Assert.AreEqual("True", channels.GetValue("rpm"));
                }
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        // an ECU's section of a program's settings
        private sealed class Section(string path) : SuiteRegistry
        {
            public override string getRegistryPath() => path;
        }

        private static void Old(string dir, string suite, string json)
        {
            Directory.CreateDirectory(Path.Combine(dir, suite));
            File.WriteAllText(Path.Combine(dir, suite, "settings.json"), json);
        }

        /// <summary>The old suites' settings into the program's, once: shared options at its top (the first suite's), the rest per section.</summary>
        [TestMethod]
        public void AnOldSuitesSettingsGoIntoItsSectionOnce()
        {
            string old = SettingsKey.BaseFolder, dir = Directory.CreateTempSubdirectory("import").FullName;
            try
            {
                SettingsKey.BaseFolder = dir;
                Old(dir, "OldA", """{ "ShowAddressesInHex": "True", "AutoFixFooter": "True", "MRUList\\0": "a.bin", "MRUList\\1": "b.bin", "LogFilters\\0\\symbol": "In.n_Engine" }""");
                File.WriteAllText(Path.Combine(dir, "OldA", "mymaps.xml"), "<maps />");
                Old(dir, "OldB", """{ "ShowAddressesInHex": "False", "AutoFixFooter": "False", "MRUList\\0": "b.bin", "MRUList\\1": "c.bin" }""");
                string[] shared = ["ShowAddressesInHex"];
                SettingsKey.ImportOnce("OldA", "Prog/A", shared);
                SettingsKey.ImportOnce("OldB", "Prog/B", shared);

                using (var prog = SettingsKey.Open("Prog"))
                {
                    // the shared option once, from the suite imported first; an own option never at the top
                    Assert.AreEqual("True", prog.GetValue("ShowAddressesInHex"));
                    Assert.IsNull(prog.GetValue("AutoFixFooter"));
                }
                using (var a = SettingsKey.Open("Prog/A"))
                {
                    Assert.AreEqual("True", a.GetValue("AutoFixFooter"));
                    Assert.IsNull(a.GetValue("ShowAddressesInHex"));
                }
                using (var b = SettingsKey.Open("Prog/B")) Assert.AreEqual("False", b.GetValue("AutoFixFooter"));
                using (var filters = SettingsKey.Open("Prog/A", @"LogFilters\0")) Assert.AreEqual("In.n_Engine", filters.GetValue("symbol"));
                // one Recent: the first suite's, then the second's new files
                using (var mru = SettingsKey.Open("Prog", "MRUList"))
                    CollectionAssert.AreEqual(new[] { "a.bin", "b.bin", "c.bin" }, new[] { "0", "1", "2" }.Select(n => (string)mru.GetValue(n)).ToArray());
                Assert.IsTrue(File.Exists(Path.Combine(SettingsKey.Folder("Prog/A"), "mymaps.xml")));

                // once: what the old suite does later stays its own
                Old(dir, "OldA", """{ "AutoFixFooter": "False" }""");
                SettingsKey.ImportOnce("OldA", "Prog/A", shared);
                using (var a = SettingsKey.Open("Prog/A")) Assert.AreEqual("True", a.GetValue("AutoFixFooter"));
            }
            finally
            {
                SettingsKey.BaseFolder = old;
                Directory.Delete(dir, true);
            }
        }

        /// <summary>Two ECUs' settings in one file: a shared option set through one is the other's too, an own option stays its own.</summary>
        [TestMethod]
        public void EcuSettingsShareTheSharedOptions()
        {
            string old = SettingsKey.BaseFolder, dir = Directory.CreateTempSubdirectory("sections").FullName;
            try
            {
                SettingsKey.BaseFolder = dir;
                var a = new AppSettings(new Section("Prog/A"));
                a.ShowAddressesInHex = true;
                a.AutoFixFooter = true;
                var b = new AppSettings(new Section("Prog/B"));
                Assert.IsTrue(b.ShowAddressesInHex);
                Assert.IsFalse(b.AutoFixFooter);
                b.AutoFixFooter = false;
                Assert.IsTrue(new AppSettings(new Section("Prog/A")).AutoFixFooter, "B's write left A's own option alone");
                using var prog = SettingsKey.Open("Prog");
                Assert.AreEqual("True", prog.GetValue("ShowAddressesInHex"));
                Assert.AreEqual("True", prog.GetValue(@"A\AutoFixFooter"));
                Assert.AreEqual("False", prog.GetValue(@"B\AutoFixFooter"));
            }
            finally
            {
                SettingsKey.BaseFolder = old;
                Directory.Delete(dir, true);
            }
        }
    }
}
