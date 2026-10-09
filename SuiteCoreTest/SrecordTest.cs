using System.IO;
using System.Runtime.CompilerServices;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TrionicCANLib.Firmware;

namespace SuiteCoreTest
{
    /// <summary>AppTool S19 to bin conversion, ported from CommonSuiteTest.</summary>
    [TestClass]
    public class SrecordTest
    {
        private static string Here([CallerFilePath] string path = "") => Path.GetDirectoryName(path);

        // ConvertSrecToBin writes the .bin next to the .s19, so convert a copy in a temp folder
        private static long Convert(string s19, uint length, bool pad)
        {
            string dir = Directory.CreateTempSubdirectory("t7srec").FullName;
            try
            {
                string input = Path.Combine(dir, s19);
                File.Copy(Path.Combine(Here(), "Resources", s19), input);
                new Srecord().ConvertSrecToBin(input, length, out string bin, pad);
                Assert.AreEqual(Path.ChangeExtension(input, ".bin"), bin);
                return new FileInfo(bin).Length;
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [TestMethod]
        public void ConvertAppToolT7s19() => Assert.AreEqual((long)FileT7.Length, Convert("t7.s19", FileT7.Length, true));

        [TestMethod]
        public void ConvertAppToolT8s19() => Assert.AreEqual((long)FileT8.Length, Convert("t8_application.s19", FileT8.Length, true));

        [TestMethod]
        public void ConvertAppToolT8s19NoPad() => Assert.AreEqual(678801L, Convert("t8_application.s19", FileT8.Length, false));
    }
}
