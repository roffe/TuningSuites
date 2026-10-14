using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace T7CoreTest
{
    /// <summary>
    /// The stock bins for a test, as copies: opening a T7 bin writes &lt;bin&gt;.xml next to it (SymbolXMLFile) and its AFR maps go into an
    /// AFRMaps folder beside it, none of which may land in T7Binaries/. Each copy has a folder of its own (Trionic7File reads any
    /// &lt;bin&gt;*.xml in the bin's folder); they all go when the run ends.
    /// </summary>
    [TestClass]
    public class Stock
    {
        private static string Here([CallerFilePath] string path = "") => Path.GetDirectoryName(path);

        public static string Folder { get; } = Path.Combine(Here(), "..", "T7Binaries");

        private static readonly string s_dir = Directory.CreateTempSubdirectory("t7stock").FullName;
        private static int s_copies;

        /// <summary>A copy of the stock bin (a file name in T7Binaries/, or a path to one).</summary>
        public static string Copy(string bin)
        {
            string dir = Directory.CreateDirectory(Path.Combine(s_dir, Interlocked.Increment(ref s_copies).ToString())).FullName;
            string file = Path.Combine(dir, Path.GetFileName(bin));
            File.Copy(Path.Combine(Folder, Path.GetFileName(bin)), file);
            return file;
        }

        [AssemblyCleanup]
        public static void DeleteCopies() => Directory.Delete(s_dir, true);
    }
}
