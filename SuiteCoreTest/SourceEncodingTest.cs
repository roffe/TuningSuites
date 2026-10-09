using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SuiteCoreTest
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
            // every project of the new solution but the submodule's; the old projects stay as they were
            string root = Path.Combine(Here(), "..");
            string[] projects = Regex.Matches(File.ReadAllText(Path.Combine(root, "TuningSuites.slnx")), "<Project Path=\"([^/\"]+)/[^\"]+\"")
                .Select(m => m.Groups[1].Value).Where(p => p != "Trionic").ToArray();
            Assert.IsGreaterThan(5, projects.Length);
            var strict = new UTF8Encoding(false, true);
            var bad = projects
                .SelectMany(p => Directory.GetFiles(Path.Combine(root, p), "*.cs", SearchOption.AllDirectories))
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .Where(f => { try { strict.GetString(File.ReadAllBytes(f)); return false; } catch (DecoderFallbackException) { return true; } })
                .ToList();
            Assert.IsEmpty(bad, "not UTF-8: " + string.Join(", ", bad));
        }
    }
}
