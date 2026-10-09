using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using T7;

namespace T7CoreTest
{
    [TestClass]
    public class DisassemblyTest
    {
        private static string Here([CallerFilePath] string path = "") => Path.GetDirectoryName(path);

        public TestContext TestContext { get; set; } = null!;

        [TestMethod]
        public void FunctionsFullAndVectors()
        {
            string dir = Directory.CreateTempSubdirectory("t7asm").FullName;
            try
            {
                T7Binary bin = T7Binary.Open(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), 0, false);
                var watch = Stopwatch.StartNew();
                string asm = Path.Combine(dir, "x.asm");
                bin.Disassemble(asm, false);
                TestContext.WriteLine($"functions: {watch.ElapsedMilliseconds} ms");
                string[] lines = File.ReadAllLines(asm);
                Assert.IsTrue(lines.Any(l => l.StartsWith("RESET_INITIAL_PROGRAM_COUNTER:") || l.StartsWith("Function_")));
                Assert.IsTrue(lines.Any(l => l.StartsWith("0x") && l.Contains('\t')));

                watch.Restart();
                string full = Path.Combine(dir, "x_full.asm");
                bin.Disassemble(full, true);
                TestContext.WriteLine($"full: {watch.ElapsedMilliseconds} ms");
                Assert.IsGreaterThan(100000, File.ReadLines(full).Count());
                Assert.IsFalse(File.ReadAllText(full).Contains(@"\par"));

                var vectors = bin.InterruptVectors();
                Assert.HasCount(256, vectors);
                Assert.AreEqual("User defined vector 191", vectors[255].Name);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
