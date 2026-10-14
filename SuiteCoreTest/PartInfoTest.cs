using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CommonSuite;

namespace SuiteCoreTest
{
    [TestClass]
    public class PartInfoTest
    {
        [TestMethod]
        public void StockBinsNextToTheProgramOrTheAppImage()
        {
            string before = Environment.GetEnvironmentVariable("APPIMAGE");
            try
            {
                Environment.SetEnvironmentVariable("APPIMAGE", null);
                Assert.AreEqual(Path.Combine(AppContext.BaseDirectory, "Binaries", "T7"), PartInfo.StockFolder("T7"));
                // run as an AppImage: its own folder is read-only, TxExtras' Binaries go next to the AppImage file
                string image = Path.Combine(Path.GetTempPath(), "apps", "TxSuite-linux-x64.AppImage");
                Environment.SetEnvironmentVariable("APPIMAGE", image);
                Assert.AreEqual(Path.Combine(Path.GetTempPath(), "apps", "Binaries", "T5"), PartInfo.StockFolder("T5"));
            }
            finally
            {
                Environment.SetEnvironmentVariable("APPIMAGE", before);
            }
        }
    }
}
