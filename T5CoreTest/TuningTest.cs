using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Trionic5Tools;
using TrionicCANLib.Checksum;

namespace T5CoreTest
{
    /// <summary>The tuning wizards' file work on a copy of a stock T5.5 bin.</summary>
    [TestClass]
    public class TuningTest
    {
        public TestContext TestContext { get; set; } = null!;

        private static T5Binary OpenCopy(string dir, string name = "4239273.BIN")
        {
            string file = Path.Combine(dir, name);
            File.Copy(BinGoldenTest.StockBins().Single(b => Path.GetFileName(b) == name), file);
            return T5Binary.Open(file);
        }

        private static void InTemp(Action<string> test)
        {
            string dir = Directory.CreateTempSubdirectory("t5tune").FullName;
            try { test(dir); }
            finally { Directory.Delete(dir, true); }
        }

        [TestMethod]
        public void TuneMeUpToStageOne() => InTemp(dir =>
        {
            T5Binary bin = OpenCopy(dir);
            var defaults = T5Tuning.TuneMeUpDefaults(bin)!;
            Assert.AreEqual(1, defaults.Stage);
            byte[] before = bin.ReadSymbol(bin.Find("Tryck_mat!"));
            var (result, report) = T5Tuning.TuneToStage(bin, 1, true);
            foreach (string l in report) TestContext.WriteLine(l);
            Assert.AreEqual(TuningResult.TuningSuccess, result);
            CollectionAssert.AreNotEqual(before, bin.ReadSymbol(bin.Find("Tryck_mat!")));
            Assert.AreEqual(TuningStage.Stage1, T5Binary.Open(bin.FileName).File.GetTrionicProperties().TuningStage);
            Assert.AreEqual(ChecksumResult.Ok, bin.VerifyChecksum());
            Assert.IsTrue(Directory.GetFiles(dir, "*beforetuningtostage1.bin").Any());
        });

        [TestMethod]
        public void MapSensorGoesToTheChosenType() => InTemp(dir =>
        {
            T5Binary bin = OpenCopy(dir);
            int constant = bin.File.GetSymbolAsInt("Inj_konst!");
            T5Tuning.ConvertMapSensor(bin, MapSensorType.MapSensor25, MapSensorType.MapSensor35, true);
            T5Binary after = T5Binary.Open(bin.FileName);
            // T5Suite always converted to 3.0 bar
            Assert.AreEqual(MapSensorType.MapSensor35, after.File.GetTrionicProperties().MapSensorType);
            Assert.AreEqual((int)Math.Floor(constant * 1.4), after.File.GetSymbolAsInt("Inj_konst!"));
            Assert.AreEqual(ChecksumResult.Ok, after.VerifyChecksum());
        });

        [TestMethod]
        public void InjectorsE85AndCodePatches() => InTemp(dir =>
        {
            T5Binary bin = OpenCopy(dir);
            var now = T5Tuning.Injectors(bin);
            Assert.AreEqual(InjectorType.Stock, now.Type);
            var to = T5Tuning.Propose(now, InjectorType.Siemens630Dekas);
            Assert.AreEqual(now.Constant - 5, to.Constant);
            T5Tuning.ApplyInjectors(bin, to, null);
            Assert.AreEqual(InjectorType.Siemens630Dekas, bin.File.GetTrionicProperties().InjectorType);
            Assert.AreEqual(now.Constant - 5, bin.File.GetSymbolAsInt("Inj_konst!"));
            Assert.AreEqual(0.33, T5Tuning.Injectors(bin).BatteryCorrection[0], 0.004);

            int constant = bin.File.GetSymbolAsInt("Inj_konst!");
            T5Tuning.ConvertToE85(bin, null, true);
            Assert.AreEqual((int)Math.Round(constant * 1.4), bin.File.GetSymbolAsInt("Inj_konst!"));
            Assert.AreEqual(1, Directory.GetFiles(dir, "*-backup-*.BIN").Length);

            var adaption = T5Tuning.ReadBoostAdaption(bin);
            Assert.IsTrue(T5Tuning.SetBoostAdaption(bin, adaption with { ManualLow = 3000, ManualHigh = 4500 }, true));
            Assert.AreEqual(3000, T5Tuning.ReadBoostAdaption(bin).ManualLow);

            var rpm = T5Tuning.ReadRpmLimits(bin)!.Value;
            T5Tuning.SetRpmLimits(bin, rpm.hardcoded + 200, rpm.software + 200, null);
            Assert.AreEqual((rpm.hardcoded + 200, rpm.software + 200), T5Tuning.ReadRpmLimits(T5Binary.Open(bin.FileName))!.Value);
            Assert.AreEqual(ChecksumResult.Ok, bin.VerifyChecksum());
        });
    
        [TestMethod]
        public void ReportsCompareAndChips() => InTemp(dir =>
        {
            T5Binary bin = OpenCopy(dir);
            List<string> examine = T5Reports.Examine(bin);
            foreach (string l in examine) TestContext.WriteLine(l);
            CollectionAssert.Contains(examine, "File type: Trionic 5.5");
            CollectionAssert.Contains(examine, "Stage: stock");
            CollectionAssert.Contains(examine, "Mapsensor type: stock 2.5 bar sensor");
            CollectionAssert.Contains(examine, "Injectors: stock");
            List<string> anomalies = T5Reports.Anomalies(bin);
            Assert.AreEqual("Checking file 4239273.BIN", anomalies[0]);

            // a stage 1 copy: the boost maps differ, SRAM-only symbols are never listed
            string stock = Path.Combine(dir, "stock.BIN");
            File.Copy(bin.FileName, stock);
            T5Tuning.TuneToStage(bin, 1, true);
            var rows = SuiteCompare.Compare(T5Binary.Open(bin.FileName), T5Binary.Open(stock));
            Assert.IsTrue(rows.Any(r => r.SymbolName == "Tryck_mat!"));
            Assert.IsTrue(rows.All(r => r.SymbolName.EndsWith('!') && !r.MissingInOriFile && !r.MissingInCompareFile), string.Join(", ", rows.Select(r => r.SymbolName)));

            byte[] data = File.ReadAllBytes(stock);
            var (chip1, chip2) = T5Binary.Split(data);
            CollectionAssert.AreEqual(data, T5Binary.Merge(chip1, chip2));
            Assert.IsNull(T5Binary.Merge(chip1, chip2[1..]));
        });

        [TestMethod]
        public void MapCsvRoundTrip() => InTemp(dir =>
        {
            T5Binary bin = OpenCopy(dir);
            foreach (string name in new[] { "Ign_map_0!", "Tryck_mat!", "Insp_mat!" })
            {
                SymbolHelper sh = bin.Find(name);
                string csv = Path.Combine(dir, name + ".csv");
                SymbolFiles.ExportMapCsv(bin, sh, csv);
                CollectionAssert.AreEqual(bin.ReadSymbol(sh), SymbolFiles.ImportMapCsv(bin, sh, csv), name);
            }
        });

        [TestMethod]
        public void MergeAdaptionData() => InTemp(dir =>
        {
            T5Binary bin = OpenCopy(dir);
            SymbolHelper adapt = bin.Find("Adapt_korr!"), fuel = bin.Find("Insp_mat!");
            byte[] before = bin.ReadSymbol(fuel), ram = new byte[0x8000];
            // 128 is "× 1.0"; the first cell gets 0 ("× 0.75")
            for (int i = 0; i < adapt.Length; i++) ram[(adapt.Start_address + i) % ram.Length] = (byte)(i == 0 ? 0 : 128);
            T5Tuning.MergeAdaption(bin, ram, new T5Tuning.AdaptionMerge(Spot: true, LongTerm: false, Idle: false), null, true);
            byte[] after = bin.ReadSymbol(fuel);
            Assert.AreEqual((byte)Math.Round(before[0] * 0.75), after[0]);
            CollectionAssert.AreEqual(before[1..], after[1..]);
            Assert.AreEqual(1, Directory.GetFiles(dir, "*beforemergingadaptiondata.bin").Length);
            Assert.AreEqual(ChecksumResult.Ok, bin.VerifyChecksum());

            // a T5.2 file lacks the adaption maps: nothing to do, no exception
            T5Binary t52 = OpenCopy(dir, "4300810.BIN");
            T5Tuning.MergeAdaption(t52, ram, new T5Tuning.AdaptionMerge(), null, true);
        });

        [TestMethod]
        public void UserLibraryScan() => InTemp(dir =>
        {
            string sub = Directory.CreateDirectory(Path.Combine(dir, "a", "b")).FullName;
            T5Binary stage1 = OpenCopy(dir);
            T5Tuning.TuneToStage(stage1, 1, true);
            File.Copy(BinGoldenTest.StockBins().First(b => Path.GetFileName(b) == "4300810.BIN"), Path.Combine(sub, "t52.BIN"));
            File.WriteAllBytes(Path.Combine(sub, "notabin.BIN"), new byte[0x40000]);
            // the stage 1 file, the tuner's backup next to it and the T5.2 bin; not the zero file
            var rows = T5UserLibrary.AddFolder([], dir);
            Assert.AreEqual(3, rows.Count, string.Join(", ", rows.Select(r => r.Name)));
            Assert.IsTrue(rows.Any(r => r.Name == "t52.BIN") && rows.All(r => r.Name != "notabin.BIN"));
            Assert.AreEqual("Stage1", rows.Single(r => r.Name == "4239273.BIN").Stage);
            Assert.AreEqual("4239273", rows.Single(r => r.Name == "4239273.BIN").Partnumber);
            // a second scan replaces the rows of the same files; the store round-trips
            Assert.AreEqual(3, T5UserLibrary.AddFolder(rows, dir).Count);
            string store = Path.Combine(dir, "UserLib.json");
            T5UserLibrary.Save(rows, store);
            Assert.AreEqual(rows.Count, T5UserLibrary.Load(store).Count);
        });
}
}
