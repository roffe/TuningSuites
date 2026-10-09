using Microsoft.VisualStudio.TestTools.UnitTesting;
using T8SuitePro;

namespace T8CoreTest
{
    [TestClass]
    public class FirmwareInfoTest
    {
        [TestMethod]
        public void EngineTypeBySoftwareVersion()
        {
            Assert.AreEqual("B207L MY2004/2005 MY03-06 Gasoline / front wheel drive", FirmwareInfo.CarInfoBySoftwareVersion("FA56_C_FME2_37_FIEF_81c"));
            Assert.AreEqual("B207R MY2007 MY07-11 Gasoline / front wheel drive", FirmwareInfo.CarInfoBySoftwareVersion("FC0J_C_FMEP_63_FIEF_82s"));
            Assert.AreEqual("B207M/F MY2011 MY10 BioPower AWD or MY11 Gasoline/BioPower FWD/AWD", FirmwareInfo.CarInfoBySoftwareVersion("FF0L_C_FMEP_31_FMEF_81t"));
            Assert.AreEqual("B207S MY2011", FirmwareInfo.CarInfoBySoftwareVersion("X_85b"));
            Assert.AreEqual("Z20NET MY03-06 Gasoline / front wheel drive", FirmwareInfo.CarInfoBySoftwareVersion("FA4Y_C_FME2_3Z_FME_PIF_83x"));
            // as T8Suite: a last part that isn't a number with a letter makes the whole text empty
            Assert.AreEqual("", FirmwareInfo.CarInfoBySoftwareVersion("FA56_C_FME2_37_FIEF_8xc"));
            Assert.AreEqual("", FirmwareInfo.CarInfoBySoftwareVersion(""));
        }
    }
}
