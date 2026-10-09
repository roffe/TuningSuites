using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using T7;

namespace T7CoreTest
{
    [TestClass]
    public class SymbolFilesTest
    {
        private static string Here([CallerFilePath] string path = "") => Path.GetDirectoryName(path);

        private string m_dir = "";

        [TestInitialize]
        public void Init() => m_dir = Directory.CreateTempSubdirectory("t7files").FullName;

        [TestCleanup]
        public void Cleanup() => Directory.Delete(m_dir, true);

        private T7Binary Copy(string name, string as_ = null)
        {
            string file = Path.Combine(m_dir, as_ ?? name);
            File.Copy(Path.Combine(Here(), "..", "T7Binaries", name), file, true);
            return T7Binary.Open(file, 0, false);
        }

        [TestMethod]
        public void S19RoundTrip()
        {
            T7Binary bin = Copy("5168646.bin");
            string s19 = Path.Combine(m_dir, "out.s19");
            Assert.IsTrue(SymbolFiles.ExportS19(bin, s19));
            Assert.StartsWith("S00600004844521B", File.ReadAllLines(s19)[0]);
            Assert.IsTrue(new Srecord().ConvertSrecToBin(s19, 0x80000, out string back, true));
            CollectionAssert.AreEqual(File.ReadAllBytes(bin.FileName), File.ReadAllBytes(back));
        }

        [TestMethod]
        public void IdcAndPackageAndCsv()
        {
            T7Binary bin = Copy("5168646.bin");
            string idc = SymbolFiles.ExportIdc(bin);
            StringAssert.Contains(File.ReadAllText(idc), "namevar(\"ROM_IgnNormCal.Map\"");

            string pkg = Path.Combine(m_dir, "stage.t7p");
            SymbolFiles.ExportPackage(bin, SymbolFiles.FixedPackage(bin), pkg);
            string[] lines = File.ReadAllLines(pkg);
            int i = System.Array.IndexOf(lines, "symbol=IgnNormCal.Map");
            Assert.IsGreaterThanOrEqualTo(0, i);
            Assert.AreEqual("length=576", lines[i + 1]);
            Assert.StartsWith("data=", lines[i + 2]);

            string csv = Path.Combine(m_dir, "symbols.csv");
            SymbolFiles.ExportSymbolCsv(bin, csv);
            Assert.HasCount(bin.Symbols.Count, File.ReadAllLines(csv));

            string map = Path.Combine(m_dir, "map.csv");
            SymbolFiles.ExportMapCsv(bin, bin.Find("IgnNormCal.Map"), map);
            string[] m = File.ReadAllLines(map);
            Assert.AreEqual("Data for IgnNormCal.Map", m[0]);
            Assert.HasCount(2 + 16, m);
            Assert.StartsWith("5820;", m[2]); // top row is the highest rpm, as the old sheet
        }

        [TestMethod]
        public void ImportNamesIntoAStrippedBin()
        {
            // a bin with stripped names, named from a CSV descriptor, then the names survive a reopen through <bin>.xml
            string stripped = Directory.GetFiles(Path.Combine(Here(), "..", "T7Binaries"), "*.bin")
                .First(f => T7Binary.Open(Copy(Path.GetFileName(f), "probe.bin").FileName, 0, false).Symbols.Cast<SymbolHelper>().Any(s => s.Varname.StartsWith("Symbolnumber ")));
            T7Binary bin = Copy(Path.GetFileName(stripped), "stripped.bin");
            SymbolHelper target = bin.Symbols.Cast<SymbolHelper>().First(s => s.Varname.StartsWith("Symbolnumber ") && s.Length > 0);
            string csv = Path.Combine(m_dir, "names.csv");
            File.WriteAllText(csv, $"{target.Symbol_number};Test.Name;;;\n");
            SymbolFiles.ImportCsv(bin, csv);
            Assert.AreEqual("Test.Name", target.Varname);
            Assert.AreEqual($"Symbolnumber {target.Symbol_number}", target.Userdescription);
            Assert.IsTrue(File.Exists(Path.Combine(m_dir, "stripped.xml")));

            T7Binary reopened = T7Binary.Open(bin.FileName, 0, false);
            Assert.IsNotNull(reopened.Symbols.Cast<SymbolHelper>().FirstOrDefault(s => s.Varname == "Test.Name"));

            // the sidecar works as an XML descriptor for another copy
            T7Binary other = Copy(Path.GetFileName(stripped), "other.bin");
            Assert.IsTrue(SymbolFiles.ImportXml(other, Path.Combine(m_dir, "stripped.xml")));
            Assert.IsNotNull(other.Symbols.Cast<SymbolHelper>().FirstOrDefault(s => s.Varname == "Test.Name"));
        }
    }
}
