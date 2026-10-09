using System;
using System.IO;
using System.Linq;
using CommonSuite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace T7CoreTest
{
    [TestClass]
    public class TransactionLogTest
    {
        private string m_dir;

        [TestInitialize]
        public void Init() => m_dir = Directory.CreateTempSubdirectory("t7ttl").FullName;

        [TestCleanup]
        public void Cleanup() => Directory.Delete(m_dir, true);

        private static TransactionEntry Entry(string note) =>
            new TransactionEntry(new DateTime(2026, 10, 9, 13, 14, 15), 0x1BA2A, 2, new byte[] { 0x10, 0x20 }, new byte[] { 0x11, 0x21 }, 0, 0, note);

        [TestMethod]
        public void RoundTrip()
        {
            var log = new TrionicTransactionLog();
            Assert.IsTrue(log.OpenTransActionLog(m_dir, "proj"));
            log.AddToTransactionLog(Entry("first"));
            log.AddToTransactionLog(Entry(""));
            log.SetEntryRolledBack(1);
            Assert.IsTrue(File.Exists(Path.Combine(m_dir, "proj", "TransActionLogV2.ttl")), "project folder and file, not a name with backslashes");

            var reopened = new TrionicTransactionLog();
            Assert.IsTrue(reopened.OpenTransActionLog(m_dir, "proj"));
            reopened.ReadTransactionFile();
            Assert.IsTrue(reopened.VerifyChecksum());
            Assert.HasCount(2, reopened.TransCollection);
            TransactionEntry first = reopened.TransCollection[0];
            Assert.AreEqual(1, first.TransactionNumber);
            Assert.IsTrue(first.IsRolledBack);
            Assert.AreEqual("first", first.Note);
            Assert.AreEqual(0x1BA2A, first.SymbolAddress);
            Assert.AreEqual(new DateTime(2026, 10, 9, 13, 14, 15), first.EntryDateTime);
            CollectionAssert.AreEqual(new byte[] { 0x10, 0x20 }, first.DataBefore);
            CollectionAssert.AreEqual(new byte[] { 0x11, 0x21 }, first.DataAfter);
            Assert.IsFalse(reopened.TransCollection[1].IsRolledBack);
        }

        [TestMethod]
        public void V2LayoutIsUnchanged()
        {
            var log = new TrionicTransactionLog();
            log.OpenTransActionLog(m_dir, "proj");
            log.AddToTransactionLog(Entry("ab"));

            byte[] entry =
            {
                1, 0, 0, 0,             // transaction number
                0,                      // rolled back
                9, 10, 0xEA, 0x07,      // day, month, year 2026
                13, 14, 15,             // hh mm ss
                0x2A, 0xBA, 0x01, 0x00, // address
                4, 0, (byte)'6', (byte)'1', (byte)'6', (byte)'2', // note "ab" as hex text
                2, 0, 0x10, 0x20, 0x11, 0x21, // length, before, after
            };
            int checksum = BitConverter.GetBytes(1).Sum(b => b) + entry.Sum(b => b);
            byte[] expected = BitConverter.GetBytes(checksum).Concat(BitConverter.GetBytes(1)).Concat(entry).ToArray();
            CollectionAssert.AreEqual(expected, File.ReadAllBytes(Path.Combine(m_dir, "proj", "TransActionLogV2.ttl")));
        }
    }
}
