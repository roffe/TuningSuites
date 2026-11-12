using Microsoft.VisualStudio.TestTools.UnitTesting;
using Trionic5Tools;

namespace T5CoreTest
{
    [TestClass]
    public class EcuMessageTest
    {
        [TestMethod]
        public void ReadFailedTextSaysWhy()
        {
            // a forum user's log: the first bootloader frame got no answer
            string noAnswer = T5Ecu.ReadFailedText(["Open called in Trionic 5", "CombiAdapter firmware v2.5", "Start ECU dump",
                "Could not start uploading bootloader... (step 0: no reply)", "Could not upload bootloader"]);
            StringAssert.StartsWith(noAnswer, "Failed to download flash from ECU\n\nOpen called in Trionic 5\n");
            StringAssert.Contains(noAnswer, "(step 0: no reply)");
            StringAssert.EndsWith(noAnswer, T5Ecu.NoAnswer);

            string adapter = T5Ecu.ReadFailedText(["Open called in Trionic 5", "CombiAdapter: Failed to connect to adapter: Access denied", "",
                "Open failed in Trionic 5"]);
            Assert.AreEqual("Failed to download flash from ECU\n\nOpen called in Trionic 5\nCombiAdapter: Failed to connect to adapter: Access denied\n"
                + "Open failed in Trionic 5", adapter);
        }
    }
}
