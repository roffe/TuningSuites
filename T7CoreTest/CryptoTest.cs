using System.IO;
using System.Runtime.CompilerServices;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace T7CoreTest
{
    [TestClass]
    public class CryptoTest
    {
        private static string Here([CallerFilePath] string path = "") => Path.GetDirectoryName(path);

        // decrypt and verify a pack the way frmMain's tuning package loader does
        private static bool Verify(string file, bool tamper)
        {
            using var sr = new StreamReader(file);
            string signature = "", content = "", s = sr.ReadLine();
            Assert.StartsWith("<SIGNATURE>", s);
            while ((s = sr.ReadLine()) != null && !s.StartsWith("</SIGNATURE>")) signature += s;
            while ((s = sr.ReadLine()) != null) content += Crypto.DecodeAES(s).Trim() + "\x0d\x0a";
            if (tamper) content = content.Replace("packname=", "packname=x");
            return Crypto.VerifyRSASignature(Crypto.CalculateMD5Hash(content), signature);
        }

        [TestMethod]
        public void ShippedPacksVerify()
        {
            string[] packs = Directory.GetFiles(Path.Combine(Here(), "..", "TuningPacks"), "*.t8x");
            Assert.IsNotEmpty(packs);
            var failed = new System.Collections.Generic.List<string>();
            foreach (string pack in packs)
            {
                if (!Verify(pack, false)) failed.Add(Path.GetFileName(pack));
                Assert.IsFalse(Verify(pack, true), pack);
            }
            // the experimental XWD pack (ba9080d) doesn't match its signature, the suites skip it at load
            CollectionAssert.AreEquivalent(new[] { "220129_All_XWD_TorqueLImDiffCode_WildcardTest_OK.t8x" }, failed, string.Join(", ", failed));
        }

        [TestMethod]
        public void Md5IsLowerHex() =>
            Assert.AreEqual("900150983cd24fb0d6963f7d28e17f72", Crypto.CalculateMD5Hash("abc"));
    }
}
