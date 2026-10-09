using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using T7;

namespace T7CoreTest
{
    [TestClass]
    public class SidInfoTest
    {
        private static string Here([CallerFilePath] string path = "") => Path.GetDirectoryName(path);

        public TestContext TestContext { get; set; } = null!;

        [TestMethod]
        public void ReadWriteAndAssign()
        {
            string dir = Directory.CreateTempSubdirectory("t7sid").FullName;
            try
            {
                string file = Path.Combine(dir, "sid.bin");
                File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), file);
                T7Binary bin = T7Binary.Open(file, 0, false);
                var rows = SidInfo.Read(bin)!;
                Assert.IsNotNull(rows);
                TestContext.WriteLine($"{rows.Count} rows: " + string.Join(", ", rows.Take(5).Select(r => $"{r.Symbol}={r.T7Symbol}@{r.AddressSRAM:X6}/{r.Value}")));
                Assert.IsTrue(rows.Count(r => r.Mode == 99) >= 30);
                Assert.IsTrue(rows.First().IsReadOnly);

                // written back unchanged: the file stays the same
                byte[] before = File.ReadAllBytes(file);
                SidInfo.Write(bin, rows, false);
                CollectionAssert.AreEqual(before, File.ReadAllBytes(file));

                // a symbol picked for the second row lands in the file
                var choice = SidInfo.Choices(bin).First(s => s.Length == 2);
                SidInfo.Assign(rows[1], choice);
                SidInfo.Write(bin, rows, false);
                var again = SidInfo.Read(T7Binary.Open(file, 0, false))!;
                Assert.AreEqual(rows[1].AddressSRAM, again[1].AddressSRAM);
                Assert.AreEqual("01", again[1].Value);

                // export / import keeps the rows
                string sid = Path.Combine(dir, "x.sid");
                SidInfo.Export(bin, again, sid);
                var imported = SidInfo.Import(bin, sid, again.Count)!;
                CollectionAssert.AreEqual(again.Select(r => r.Symbol).ToArray(), imported.Select(r => r.Symbol).ToArray());
                Assert.IsNull(SidInfo.Import(bin, sid, again.Count + 1));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
