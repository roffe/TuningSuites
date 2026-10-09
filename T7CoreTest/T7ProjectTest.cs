using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using T7;
using TrionicCANLib.Checksum;

namespace T7CoreTest
{
    [TestClass]
    public class T7ProjectTest
    {
        private static string Here([CallerFilePath] string path = "") => Path.GetDirectoryName(path);

        [TestMethod]
        public void CreateEditRollBackAndRebuild()
        {
            string root = Directory.CreateTempSubdirectory("t7proj").FullName;
            try
            {
                string source = Path.Combine(root, "5168646.bin");
                File.Copy(Path.Combine(Here(), "..", "T7Binaries", "5168646.bin"), source);
                string folder = Path.Combine(root, "Projects");

                string name = T7Project.Create(folder, new ProjectProperties("SAAB", "9-5", "2002", "", "5168646 EA1WF0LC:47D", "", "1.00.000"), source);
                Assert.AreEqual("5168646 EA1WF0LC47D", name); // ':' dropped
                Assert.ThrowsExactly<InvalidOperationException>(() => T7Project.Create(folder, new ProjectProperties("", "", "", "", name, "", ""), source));
                string xml = File.ReadAllText(T7Project.PropertiesFile(Path.Combine(folder, name)));
                StringAssert.Contains(xml, "<DocumentElement>");
                StringAssert.Contains(xml, "<T5PROJECT>");
                StringAssert.Contains(xml, "<CARMAKE>SAAB</CARMAKE>");

                T7Project project = T7Project.Open(folder, name);
                Assert.IsNotNull(project);
                Assert.IsTrue(File.Exists(project.BinaryFile));
                project.CreateBackup();
                project.CreateBackup(); // same second: numbered, not an exception
                Assert.HasCount(1, T7Project.List(folder));
                Assert.AreEqual("2", T7Project.List(folder)[0].NumberBackups);

                // a map save with a transaction entry and a logbook line
                T7Binary bin = T7Binary.Open(project.BinaryFile, 0, false);
                SymbolHelper map = bin.Find("IgnNormCal.Map");
                int address = bin.FileAddress(map);
                byte[] original = bin.ReadSymbol(map);
                byte[] changed = (byte[])original.Clone();
                changed[0] ^= 0x01;
                bin.WriteSymbol(address, changed, false, project.TransactionLog, "richer");
                project.LogTransaction(bin, project.TransactionLog.TransCollection[0]);
                Assert.AreSame(project.TransactionLog.TransCollection[0], project.UndoTarget);

                // roll back: the old bytes, a valid checksum, the flag; roll forward undoes that
                project.Roll(bin, project.UndoTarget, back: true, autoFixFooter: false);
                CollectionAssert.AreEqual(original, bin.ReadSymbol(map));
                Assert.AreEqual(ChecksumResult.Ok, bin.VerifyChecksum());
                Assert.IsTrue(project.TransactionLog.TransCollection[0].IsRolledBack);
                Assert.IsNull(project.UndoTarget);
                project.Roll(bin, project.RedoTarget, back: false, autoFixFooter: false);
                CollectionAssert.AreEqual(changed, bin.ReadSymbol(map));

                var log = project.ReadLogbook();
                CollectionAssert.IsSubsetOf(new[] { "A backup file was created", "A transaction was executed", "A transaction was rolled back", "A transaction rolled forward" },
                    log.Select(l => l.Type).Distinct().ToArray());
                Assert.IsTrue(log.Any(l => l.Description.StartsWith("IgnNormCal.Map richer")));

                // rename moves the folder and the binary path
                project.Edit(project.Properties with { Name = "renamed", Version = "1.01" });
                Assert.IsTrue(Directory.Exists(Path.Combine(folder, "renamed")));
                Assert.IsTrue(File.Exists(project.BinaryFile));
                Assert.AreEqual("1.01", T7Project.Open(folder, "renamed").Properties.Version);

                // rebuild up to now from the newest backup: the transaction applied, checksum valid
                string rebuilt = project.Rebuild(DateTime.Now.AddMinutes(1), false);
                Assert.AreEqual(ChecksumResult.Ok, T7Binary.OpenRaw(rebuilt).VerifyChecksum());
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }
}
