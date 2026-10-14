using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NLog;
using NLog.Config;
using NLog.Targets;
using SuiteApp.Services;
using SuiteApp.ViewModels;

namespace TxSuiteTest
{
    /// <summary>Settings → Logging: the logs' levels into NLog's rules, the old dated files deleted, the tab's rows saved.</summary>
    public partial class SwitchTest
    {
        private static Logging.LogFile Log(string target) => Logging.Files.Single(f => f.Target == target);

        // back to the logs' own levels, so other tests see NLog.config's
        private static void DefaultLogging()
        {
            foreach (Logging.LogFile log in Logging.Files) Logging.Set(log, log.Level, log.On);
            Logging.KeepDays = 0;
        }

        [TestMethod]
        public void LogLevelsGoIntoNLogsRules()
        {
            var config = new LoggingConfiguration();
            Target ui = new NullTarget("uilogfile"), can = new NullTarget("canlogfile"), kwp = new NullTarget("kwplogfile"), debugger = new NullTarget("debugger");
            var program = new LoggingRule("T7.*", LogLevel.Trace, ui);
            var core = new LoggingRule("Trionic5Tools.*", LogLevel.Trace, ui);
            var frames = new LoggingRule("TrionicCANLib.CAN.*", LogLevel.Trace, LogLevel.Trace, can);
            var kwpRule = new LoggingRule("TrionicCANLib.KWP.*", LogLevel.Trace, LogLevel.Trace, kwp);
            var debug = new LoggingRule("T7.*", LogLevel.Trace, debugger);
            foreach (var rule in new[] { program, core, frames, kwpRule, debug }) config.LoggingRules.Add(rule);
            try
            {
                // as NLog.config set them up: the CAN frames off
                DefaultLogging();
                Logging.Apply(config);
                Assert.IsEmpty(frames.Levels);
                CollectionAssert.AreEqual(new[] { LogLevel.Trace, LogLevel.Debug, LogLevel.Info, LogLevel.Warn, LogLevel.Error, LogLevel.Fatal }, program.Levels.ToArray());

                // a level and the ones after it, in every rule writing to the log; off is nothing; the debugger's output as it was
                Logging.Set(Log("uilogfile"), "Warn", true);
                Logging.Set(Log("canlogfile"), "Trace", true);
                Logging.Set(Log("kwplogfile"), "Debug", false);
                Logging.Apply(config);
                foreach (var rule in new[] { program, core })
                    CollectionAssert.AreEqual(new[] { LogLevel.Warn, LogLevel.Error, LogLevel.Fatal }, rule.Levels.ToArray());
                CollectionAssert.Contains(frames.Levels.ToList(), LogLevel.Trace);
                Assert.IsEmpty(kwpRule.Levels);
                Assert.HasCount(6, debug.Levels);
            }
            finally
            {
                DefaultLogging();
            }
        }

        [TestMethod]
        public void OldLogFilesGo()
        {
            string folder = Directory.CreateDirectory(Path.Combine(s_dir, "logs")).FullName;
            var now = new DateTime(2026, 10, 14, 12, 0, 0);
            string File(string name, int daysOld)
            {
                string file = Path.Combine(folder, name);
                System.IO.File.WriteAllText(file, "x");
                System.IO.File.SetLastWriteTime(file, now.AddDays(-daysOld));
                return file;
            }
            string[] old = [File("uiLog2026-09-01.txt", 43), File("canLog2026-10-01.txt", 13)];
            string[] kept = [File("uiLog2026-10-14.txt", 0), File("deviceLog2026-10-10.txt", 4), File("settings.json", 60), File("notes.txt", 60), File("uiLog-old.txt", 60)];
            try
            {
                // 0 keeps them all
                Assert.AreEqual(0, Logging.DeleteOld(folder, now));
                Logging.KeepDays = 7;
                Assert.AreEqual(2, Logging.DeleteOld(folder, now));
                Assert.IsFalse(old.Any(System.IO.File.Exists));
                // only the logs' dated files, and only old ones
                Assert.IsTrue(kept.All(System.IO.File.Exists));
            }
            finally
            {
                DefaultLogging();
                Directory.Delete(folder, true);
            }
        }

        [TestMethod]
        public void LoggingTabSavesItsRows()
        {
            try
            {
                DefaultLogging();
                var tab = new LoggingSettingsViewModel();
                CollectionAssert.AreEqual(new[] { "Program", "ECU sessions", "KWP requests", "Flashing", "CAN adapter", "CAN frames" }, tab.Logs.Select(l => l.Caption).ToArray());
                var frames = tab.Logs.Single(l => l.Log.Target == "canlogfile");
                Assert.IsFalse(frames.On);
                frames.On = true;
                frames.Level = "Debug";
                tab.KeepDays = 14;
                tab.Apply();

                var again = new LoggingSettingsViewModel();
                var saved = again.Logs.Single(l => l.Log.Target == "canlogfile");
                Assert.IsTrue(saved.On);
                Assert.AreEqual("Debug", saved.Level);
                Assert.AreEqual(14m, again.KeepDays);
                StringAssert.Contains(saved.Tip, "canLog<date>.txt");
            }
            finally
            {
                DefaultLogging();
            }
        }
    }
}
