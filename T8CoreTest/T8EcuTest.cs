using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using T8SuitePro;

namespace T8CoreTest
{
    /// <summary>T8Ecu without an ECU: its thread, the adapter check before anything goes on the wire, the fault code lines.</summary>
    [TestClass]
    public class T8EcuTest
    {
        [TestMethod]
        public async Task EveryCallRunsOnTheOneEcuThread()
        {
            using var ecu = new T8Ecu();
            int a = await ecu.RunAsync(_ => Environment.CurrentManagedThreadId);
            int b = await Task.Run(() => ecu.RunAsync(_ => Environment.CurrentManagedThreadId));
            Assert.AreEqual(a, b);
            Assert.AreNotEqual(Environment.CurrentManagedThreadId, a);
            Assert.AreEqual("T8 ECU", await ecu.RunAsync(_ => Thread.CurrentThread.Name));
        }

        [TestMethod]
        public async Task WithoutAnAdapterNothingStarts()
        {
            string dir = Directory.CreateTempSubdirectory("t8ecu").FullName;
            string before = SettingsKey.BaseFolder;
            SettingsKey.BaseFolder = dir;
            try
            {
                var settings = new AppSettings(new T8SuiteRegistry()) { AdapterType = "Lawicel CANUSB", Adapter = "" };
                using var ecu = new T8Ecu();
                var e = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => ecu.ConnectAsync(settings));
                Assert.AreEqual("Check settings, no CAN adapter has been selected!", e.Message);
                Assert.IsFalse(ecu.IsConnected);

                // a flasher session gives the adapter back: not busy afterwards, so the app can close
                await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => ecu.ReadFlashAsync(settings, Path.Combine(dir, "read.bin")));
                Assert.IsFalse(ecu.IsFlashing);
                await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => ecu.ReadFaultCodesAsync(settings));
            }
            finally
            {
                SettingsKey.BaseFolder = before;
                Directory.Delete(dir, true);
            }
        }

        [TestMethod]
        public void FaultCodesFromReadDtc()
        {
            CollectionAssert.AreEqual(new[] { "P0300", "U0100" },
                T8Ecu.FaultCodes(["DTC: P0300 StatusByte: 08", "DTC: U0100 StatusByte: 09", "DTC: P0300 StatusByte: 08", "No more errors!"]));
            Assert.IsEmpty(T8Ecu.FaultCodes(null));
        }

        [TestMethod]
        public void CatalogHasT8SuitesDescriptions()
        {
            var catalog = DtcCatalog.Load();
            Assert.IsGreaterThan(500, catalog.Count);
            // seven-character WIS codes are looked up by their first five
            Assert.AreEqual("Crankshaft Position Sensor Circuit, Crank Time Based Circuit", catalog["P0335"].Description);
        }
    }
}
