using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using T8SuitePro;
using TrionicCANLib.Checksum;

namespace T8CoreTest
{
    /// <summary>The shared offline tuning (compare, transfer, search, packages, imports, exports) on T8 bins.</summary>
    [TestClass]
    public class OfflineTest
    {
        private const string Old = "55353231_FA56_C_FME2_37_FIEF_81c.BIN", NewPetrol = "55565020_FC0J_C_FMEP_63_FIEF_82s.BIN",
            BioPower = "55565020_FD0I_C_FMEP_33_FIEF_81l.BIN", Stripped = "55352688_FA4H_C_FME9_28_SAN_PF_81b.BIN";

        private static string NoSymbolLists => Path.Combine(Path.GetTempPath(), "t8golden-no-symbol-lists");

        private static string Stock(string name) => BinGoldenTest.StockBins().Single(b => Path.GetFileName(b) == name);

        private static string Copy(string dir, string name, string as_ = null)
        {
            string file = Path.Combine(dir, as_ ?? name);
            File.Copy(Stock(name), file);
            return file;
        }

        private static T8Binary Open(string file) => T8Binary.Open(file, false, NoSymbolLists);

        [TestMethod]
        public void CompareTransferAndBinaryDiff()
        {
            string dir = Directory.CreateTempSubdirectory("t8cmp").FullName;
            try
            {
                string a = Copy(dir, Old, "a.bin"), b = Copy(dir, Old, "b.bin");
                T8Binary binA = Open(a);
                Assert.IsEmpty(SuiteCompare.Compare(binA, Open(b)));

                T8Binary binB = Open(b);
                SymbolHelper map = binB.Find("IgnAbsCal.fi_NormalMAP");
                byte[] data = binB.ReadSymbol(map), changed = (byte[])data.Clone();
                changed[1]++;
                binB.WriteSymbol(binB.FileAddress(map), changed);

                CompareRow row = SuiteCompare.Compare(binA, Open(b)).Single();
                Assert.AreEqual("IgnAbsCal.fi_NormalMAP", row.SymbolName);
                Assert.AreEqual(1, row.Differences);
                Assert.AreEqual("IgnAbsCal", row.Category);
                Assert.AreNotEqual("", row.Description);

                // the map's line differs; outside the symbols only the checksums do
                string line = $"{binA.FileAddress(map) & ~0xF:X6}:";
                Assert.IsTrue(SuiteCompare.BinaryDiff(a, b).Any(l => l.current.StartsWith(line)));
                var outside = SuiteCompare.BinaryDiff(a, b, binA.Symbols);
                Assert.IsNotEmpty(outside);
                Assert.IsFalse(outside.Any(l => l.current.StartsWith(line)));

                CollectionAssert.Contains(SuiteCompare.TransferCandidates(binA), "IgnAbsCal.fi_NormalMAP");
                List<string> report = SuiteCompare.TransferMaps(binA, b, Open, new HashSet<string> { "IgnAbsCal.fi_NormalMAP" });
                CollectionAssert.Contains(report, "Transferred symbol IgnAbsCal.fi_NormalMAP successfully");
                CollectionAssert.AreEqual(data, Open(b).ReadSymbol(map));
                Assert.AreEqual(ChecksumResult.Ok, Open(b).VerifyChecksum());
                Assert.HasCount(1, Directory.GetFiles(dir, "*beforetransferringmaps.bin"));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [TestMethod]
        public void QuickMapsFollowTheCalibration()
        {
            List<MapShortcut> old = Open(Stock(Old)).QuickMaps(), petrol = Open(Stock(NewPetrol)).QuickMaps(), bio = Open(Stock(BioPower)).QuickMaps();
            string Symbol(List<MapShortcut> maps, string caption) => maps.SingleOrDefault(m => m.Caption == caption)?.Symbol;

            Assert.AreEqual("BstKnkCal.MaxAirmassAu", Symbol(old, "Max airmass map (auto)"));
            Assert.AreEqual("TrqLimCal.Trq_MaxEngineManTab2", Symbol(old, "Trq limit manual 150 hp"));
            Assert.IsNull(Symbol(old, "Max torque 150hp"));
            Assert.IsNull(Symbol(old, "FlexFuel torque limit"));

            Assert.AreEqual("FFAirCal.m_maxAirmass", Symbol(petrol, "Max air E85"));
            Assert.AreEqual("TrqLimCal.Trq_MaxEngineTab2", Symbol(petrol, "Trq limit 150hp"));
            Assert.AreEqual("TMCCal.Trq_MaxEngineLowTab", Symbol(petrol, "Max torque 150hp"));
            Assert.IsNull(Symbol(petrol, "Trq limit E85 150hp"));
            Assert.IsNull(Symbol(petrol, "FlexFuel torque limit"));

            Assert.AreEqual("FFTrqCal.FFTrq_MaxEngineTab2", Symbol(bio, "Trq limit E85 150hp"));
            Assert.AreEqual("FFTrqCal.M_maxMAP", Symbol(bio, "FlexFuel torque limit"));
            CollectionAssert.AreEqual(new[] { "Airmass controller", "Torque controller", "Fuel controller", "Boost controller", "Ignition controller", "Pedal controller", "General" },
                bio.Select(m => m.Group).Distinct().ToArray());
        }

        [TestMethod]
        public void SearchPackagesAndExports()
        {
            string dir = Directory.CreateTempSubdirectory("t8pkg").FullName;
            try
            {
                T8Binary bin = Open(Copy(dir, Old));
                SymbolHelper map = bin.Find("IgnAbsCal.fi_NormalMAP");
                byte[] data = bin.ReadSymbol(map);
                int last = data[^2] << 8 | data[^1];
                var byValue = MapSearch.Find(bin, new MapSearchOptions(true, (decimal)(last * 0.1f), false, "", false, false, true, map.Length));
                CollectionAssert.Contains(byValue, map);
                CollectionAssert.Contains(MapSearch.Find(bin, new MapSearchOptions(false, 0, true, "fi_NormalMAP", true, false, false, 0)), map);

                // the fixed package puts a changed map back, with one checksum update and the WIZARD log
                string pkg = Path.Combine(dir, "fixed.t8p");
                SymbolFiles.ExportPackage(bin, SymbolFiles.FixedPackage(bin), pkg);
                var maps = SymbolFiles.ReadPackage(pkg);
                Assert.IsGreaterThan(20, maps.Count);
                CollectionAssert.Contains(maps.Select(m => m.name).ToList(), "IgnAbsCal.fi_NormalMAP");
                byte[] changed = data.Select(x => (byte)(x ^ 1)).ToArray();
                bin.WriteSymbol(bin.FileAddress(map), changed);
                List<PackageResult> results = TuningPackage.Read(pkg, bin).Apply(bin);
                Assert.IsTrue(results.All(r => r.Success), string.Join(", ", results.Where(r => !r.Success).Select(r => r.Map + " " + r.Detail)));
                CollectionAssert.AreEqual(data, bin.ReadSymbol(map));
                Assert.AreEqual(ChecksumResult.Ok, bin.VerifyChecksum());
                Assert.HasCount(1, Directory.GetFiles(dir, "*-WIZARD.log"));

                string s19 = Path.Combine(dir, "a.S19");
                Assert.IsTrue(SymbolFiles.ExportS19(bin, s19));
                StringAssert.StartsWith(File.ReadLines(s19).First(), "S0");

                string csv = Path.Combine(dir, "map.csv");
                SymbolFiles.ExportMapCsv(bin, map, csv);
                string[] lines = File.ReadAllLines(csv);
                Assert.AreEqual("Data for IgnAbsCal.fi_NormalMAP", lines[0]);
                Assert.HasCount(2 + 16, lines);

                string idc = bin.ExportIdc();
                StringAssert.EndsWith(idc, "-autogen.idc");
                StringAssert.Contains(File.ReadAllText(idc), "namevar(\"ROM_IgnAbsCal_fi_NormalMAP\", 0xAA5EE, 0x240);");
                Assert.IsFalse(File.ReadLines(idc).Any(l => l.Contains("namevar(\"") && l.Split('"')[1].Length > 4 + 30 + 4));

                // T8Suite's symbol list export: six fields, no user description
                string list = Path.Combine(dir, "symbols.csv");
                SymbolFiles.ExportSymbolCsv(bin, list, false);
                Assert.IsTrue(File.ReadLines(list).All(l => l.Count(c => c == ',') == 5));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        /// <summary>
        /// T8's "Symbolnumber N" placeholder is one less than Symbol_number, which the sidecar's swap-back test missed: an imported
        /// name was written under its own name and lost on the next open.
        /// </summary>
        [TestMethod]
        public void ImportedNamesSurviveTheSidecar()
        {
            string dir = Directory.CreateTempSubdirectory("t8names").FullName;
            try
            {
                string file = Copy(dir, Stripped);
                T8Binary bin = Open(file);
                SymbolHelper sh = bin.Symbols.Cast<SymbolHelper>().Single(s => s.Varname == "Symbolnumber 10");
                Assert.AreEqual(11, sh.Symbol_number);
                string csv = Path.Combine(dir, "names.csv");
                File.WriteAllText(csv, "11;My.Name\n");
                SymbolFiles.ImportCsv(bin, csv);
                Assert.AreEqual("My.Name", sh.SmartVarname);

                for (int open = 0; open < 2; open++)
                {
                    bin = Open(file);
                    Assert.AreEqual("My.Name", bin.Symbols.Cast<SymbolHelper>().Single(s => s.Symbol_number == 11).SmartVarname);
                    // a user description edit saves the sidecar again
                    SymbolXMLFile.SaveAdditionalSymbols(bin.FileName, bin.Symbols);
                }
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [TestMethod]
        public void CopyAddressTable()
        {
            string dir = Directory.CreateTempSubdirectory("t8addr").FullName;
            try
            {
                string a = Copy(dir, Old, "a.bin"), b = Copy(dir, Old, "b.bin");
                T8Binary bin = Open(a);
                int start = bin.AddressTableStart(a);
                Assert.AreEqual(start, bin.AddressTableStart(b));
                byte[] target = File.ReadAllBytes(b);
                target[start - 17] ^= 0xFF;
                File.WriteAllBytes(b, target);

                bin.CopyAddressTable(b);
                CollectionAssert.AreEqual(File.ReadAllBytes(a)[(start - 17)..start], File.ReadAllBytes(b)[(start - 17)..start]);
                Assert.AreEqual(ChecksumResult.Ok, Open(b).VerifyChecksum());
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    
        [TestMethod]
        public void FirmwareEdits()
        {
            string dir = Directory.CreateTempSubdirectory("t8fw").FullName;
            try
            {
                string file = Copy(dir, Old);
                T8Binary bin = Open(file);
                var log = new TrionicTransactionLog();
                log.OpenTransActionLog(dir, "proj");

                // a short immobilizer code: T8Suite threw after the VIN was already written
                FirmwareInfo.Apply(bin, new FirmwareEdit(null, "YS3FB45F931012345", "1234"), log);
                FirmwareInfo after = FirmwareInfo.Read(file);
                Assert.AreEqual("YS3FB45F931012345", after.ChassisId);
                Assert.AreEqual("1234", after.SerialNumber.Trim());
                Assert.IsNotEmpty(log.TransCollection);
                Assert.AreEqual("Firmware information", log.TransCollection[0].Note);

                FirmwareInfo.Apply(bin, new FirmwareEdit("FA56_C_FME2_37_FIEF_81x", null, null));
                Assert.AreEqual("FA56_C_FME2_37_FIEF_81x", FirmwareInfo.Read(file).SoftwareVersion);
                Assert.AreEqual("YS3FB45F931012345", FirmwareInfo.Read(file).ChassisId);
                // the MFS area and the PI containers lie outside both checksum layers
                Assert.AreEqual(ChecksumResult.Ok, bin.VerifyChecksum());
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
