using System;
using System.IO;
using System.Linq;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using T7;

namespace T7CoreTest
{
    [TestClass]
    public class T7LogFileTest
    {
        [TestMethod]
        public void SectionsFiltersAndExports()
        {
            string dir = Directory.CreateTempSubdirectory("t7log").FullName;
            string before = SettingsKey.BaseFolder;
            SettingsKey.BaseFolder = dir;
            try
            {
                var t0 = new DateTime(2026, 10, 9, 12, 0, 0);
                string log = Path.Combine(dir, "bin-20261009-CanTraceExt.t7l");
                File.WriteAllLines(log,
                [
                    T7Log.Line(t0, [("ActualIn.n_Engine", 900), ("In.p_AirInlet", 0.1)], false),
                    T7Log.Line(t0.AddSeconds(1), [("ActualIn.n_Engine", 2500), ("In.p_AirInlet", 0.8)], true),
                    "garbage",
                    // a second session after a 30 s gap; this line has no boost
                    T7Log.Line(t0.AddSeconds(31), [("ActualIn.n_Engine", 3000)], false),
                    T7Log.Line(t0.AddSeconds(32), [("ActualIn.n_Engine", 3100), ("In.p_AirInlet", 1.2)], false),
                ]);
                var lines = T7LogFile.Read(log);
                Assert.HasCount(4, lines);
                var sections = T7LogFile.Sections(lines);
                Assert.HasCount(2, sections);
                Assert.HasCount(2, sections[1]);                      // the first line after the gap is kept
                Assert.AreEqual("12:00:31 - 12:00:32 [00:00:01]", T7LogFile.Describe(sections[1]));
                CollectionAssert.AreEqual(new[] { "ActualIn.n_Engine", "In.p_AirInlet", "IMPORTANTLINE" }, T7LogFile.Symbols(lines));
                Assert.AreEqual("Rpm", T7LogFile.DisplayName("ActualIn.n_Engine"));

                var rpmAbove = new LogFilter { Active = true, Symbol = "ActualIn.n_Engine", Type = LogFilter.MathType.GreaterThan, Value = 2000 };
                Assert.HasCount(3, lines.Where(l => T7LogFile.Passes(l, [rpmAbove])));

                // CSV: a missing value leaves its column empty
                string csv = Path.Combine(dir, "out.csv");
                T7LogFile.ExportCsv(sections[1], ["ActualIn.n_Engine", "In.p_AirInlet"], csv);
                CollectionAssert.AreEqual(new[] { "Time,ActualIn.n_Engine,In.p_AirInlet", "0.0000,3000,", "1.0000,3100,1.2" }, File.ReadAllLines(csv));

                // filters are stored, and a removed one stays removed
                var store = new LogFilters(new T7SuiteRegistry());
                var filters = new LogFilterCollection { rpmAbove, new LogFilter { Index = 1, Symbol = "In.p_AirInlet", Value = 0.5f } };
                store.SaveFiltersToRegistry(filters);
                Assert.HasCount(2, store.GetFiltersFromRegistry());
                store.SaveFiltersToRegistry(new LogFilterCollection { rpmAbove });
                var back = store.GetFiltersFromRegistry().Cast<LogFilter>().Single();
                Assert.AreEqual(2000, back.Value);

                // the LogWorks export reads our invariant numbers
                var dif = new DifGenerator { AppSettings = new AppSettings(new T7SuiteRegistry()) };
                dif.SetFilters(new LogFilterCollection());
                var symbols = new SymbolCollection { new SymbolHelper { Varname = "In.p_AirInlet" } };
                Assert.IsTrue(dif.ConvertFileToDif(log, symbols, t0, t0.AddMinutes(1), false, false));
                StringAssert.Contains(File.ReadAllText(Path.ChangeExtension(log, ".dif")), "0,0.800");
            }
            finally
            {
                SettingsKey.BaseFolder = before;
                Directory.Delete(dir, true);
            }
        }
    }
}
