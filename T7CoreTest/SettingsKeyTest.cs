using System.Globalization;
using System.IO;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace T7CoreTest
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
    }
}
