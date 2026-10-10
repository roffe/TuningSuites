using System;
using System.IO;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Trionic5Tools;

namespace T5CoreTest
{
    [TestClass]
    public class LogWorksExportTest
    {
        [TestMethod]
        public void NarrowbandSondAsLogged()
        {
            string dir = Directory.CreateTempSubdirectory("t5dif").FullName;
            string before = SettingsKey.BaseFolder;
            SettingsKey.BaseFolder = dir;
            try
            {
                // the panel logs AD_sond as λ: raw 30 is 0.95
                double lambda = T5Realtime.Rules.Decode(new RealtimeSymbol { Name = "AD_sond" }, [30]);
                Assert.AreEqual(0.95, lambda, 1e-9);
                var t0 = new DateTime(2026, 10, 9, 12, 0, 0);
                string log = Path.Combine(dir, "bin-20261009-CanTraceExt.t5l");
                File.WriteAllLines(log, [RealtimeLog.Line(t0, [("Rpm", 3000), ("AD_sond", lambda)], false)]);

                // Export logfile to LogWorks writes it as it is, not converted from A/D counts a second time (1.240)
                var dif = new DifGenerator { AppSettings = new AppSettings(new T5SuiteRegistry()) };
                dif.SetFilters(new LogFilterCollection());
                var symbols = new SymbolCollection { new SymbolHelper { Varname = "AD_sond" } };
                Assert.IsTrue(dif.ConvertFileToDif(log, symbols, t0, t0.AddMinutes(1), false, false));
                StringAssert.Contains(File.ReadAllText(Path.ChangeExtension(log, ".dif")), "0,0.950");
            }
            finally
            {
                SettingsKey.BaseFolder = before;
                Directory.Delete(dir, true);
            }
        }
    }
}
