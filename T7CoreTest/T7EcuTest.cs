using System;
using System.Threading;
using System.Threading.Tasks;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using T7;

namespace T7CoreTest
{
    [TestClass]
    public class T7EcuTest
    {
        [TestMethod]
        public async Task EveryCallRunsOnTheOneEcuThread()
        {
            using var ecu = new T7Ecu();
            int a = await ecu.RunAsync(_ => Environment.CurrentManagedThreadId);
            int b = await Task.Run(() => ecu.RunAsync(_ => Environment.CurrentManagedThreadId));
            Assert.AreEqual(a, b);
            Assert.AreNotEqual(Environment.CurrentManagedThreadId, a);
            Assert.AreEqual("T7 ECU", await ecu.RunAsync(_ => Thread.CurrentThread.Name));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => ecu.RunAsync<int>(_ => throw new InvalidOperationException("boom")));
        }

        [TestMethod]
        public async Task ConnectWithoutAnAdapterSaysSo()
        {
            string dir = System.IO.Directory.CreateTempSubdirectory("t7ecu").FullName;
            string before = SettingsKey.BaseFolder;
            SettingsKey.BaseFolder = dir;
            try
            {
                var settings = new AppSettings(new T7SuiteRegistry()) { AdapterType = "Lawicel CANUSB", Adapter = "" };
                using var ecu = new T7Ecu();
                var e = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => ecu.ConnectAsync(settings));
                Assert.AreEqual("Check settings, no CAN adapter has been selected!", e.Message);
                Assert.IsFalse(ecu.IsConnected);
                CollectionAssert.Contains(CanAdapters.Types, "SLCAN");
            }
            finally
            {
                SettingsKey.BaseFolder = before;
                System.IO.Directory.Delete(dir, true);
            }
        }
    }
}

namespace T7CoreTest
{
    [Microsoft.VisualStudio.TestTools.UnitTesting.TestClass]
    public class FaultCodeTest
    {
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void ParsesPairsUntilZero() =>
            Microsoft.VisualStudio.TestTools.UnitTesting.CollectionAssert.AreEqual(new[] { "P0300", "P1A0F" },
                T7.T7Ecu.FaultCodes([0x03, 0x00, 0x1A, 0x0F, 0x00, 0x00, 0x12, 0x34]));

        [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public void CatalogHasTheShippedDescriptions()
        {
            var catalog = CommonSuite.DtcCatalog.Load();
            Microsoft.VisualStudio.TestTools.UnitTesting.Assert.IsGreaterThan(100, catalog.Count);
            Microsoft.VisualStudio.TestTools.UnitTesting.Assert.AreEqual("Ox front sensor preheater control circuit", catalog["P0030"].Description);
        }
    }
}
