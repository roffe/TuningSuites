using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace T7CoreTest
{
    /// <summary>
    /// The old suites were compiled with the Windows ANSI code page, the .NET SDK reads sources as UTF-8. A lifted file still
    /// in Windows-1252 compiles fine but garbles its non-ASCII literals ("Trollhättan"), so convert it with iconv -f CP1252.
    /// </summary>
    [TestClass]
    public class SourceEncodingTest
    {
        private static string Here([CallerFilePath] string path = "") => Path.GetDirectoryName(path);

        [TestMethod]
        public void LiftedSourcesAreUtf8()
        {
            var strict = new UTF8Encoding(false, true);
            var bad = new[] { "T7Core", "T7CoreTest", "T7App" }
                .SelectMany(p => Directory.GetFiles(Path.Combine(Here(), "..", p), "*.cs", SearchOption.AllDirectories))
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .Where(f => { try { strict.GetString(File.ReadAllBytes(f)); return false; } catch (DecoderFallbackException) { return true; } })
                .ToList();
            Assert.IsEmpty(bad, "not UTF-8: " + string.Join(", ", bad));
        }
    }
}
