using System;
using System.Collections.Generic;
using CommonSuite;
using NLog;

namespace T8SuitePro
{
    /// <summary>A row of the flash block browser (frmFlashBlockBrowser).</summary>
    public record FlashBlockRow(int Blocknumber, string Blocktype, string Address, string VIN, string EcuType, string Interface, string SecretCode);

    /// <summary>
    /// What T8Suite's firmware information shows (Form1.barButtonItem5_ItemClick, frmFirmwareInformation), read from the file each
    /// time as T8Suite did: the PI area, the MFS info record and the last valid flash block (T8Header), the engine read from the
    /// software version and from the VIN.
    /// </summary>
    public sealed class FirmwareInfo
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public string EngineTypeBySoftwareVersion { get; init; } = "";
        public string EngineTypeByVin { get; init; } = "";
        public string SoftwareVersion { get; init; } = "";
        public string PartNumber { get; init; } = "";
        public string SerialNumber { get; init; } = "";
        public string ChassisId { get; init; } = "";
        public string ProgrammingDevice { get; init; } = "";
        public string ProgrammerName { get; init; } = "";
        public string ReleaseDate { get; init; } = "";
        public string HardwareId { get; init; } = "";
        public string HardwareType { get; init; } = "";
        public string EcuDescription { get; init; } = "";
        public string InterfaceDevice { get; init; } = "";
        public int FlashBlockCount { get; init; }

        public static FirmwareInfo Read(string file)
        {
            var header = new T8Header();
            header.init(file);
            VINCarInfo car = VINDecoder.DecodeVINNumber(header.ChassisID);
            return new FirmwareInfo
            {
                EngineTypeBySoftwareVersion = CarInfoBySoftwareVersion(header.SoftwareVersion.Trim()),
                EngineTypeByVin = car.EngineType + " MY" + car.Makeyear + " " + car.GearboxDescription,
                SoftwareVersion = header.SoftwareVersion.Trim(),
                PartNumber = header.PartNumber,
                SerialNumber = header.SerialNumber,
                ChassisId = header.ChassisID,
                ProgrammingDevice = header.ProgrammerDevice,
                ProgrammerName = header.ProgrammerName,
                ReleaseDate = header.ReleaseDate,
                HardwareId = header.HardwareID,
                HardwareType = header.DeviceType,
                EcuDescription = header.EcuDescription,
                InterfaceDevice = header.InterfaceDevice,
                FlashBlockCount = header.NumberOfFlashBlocks,
            };
        }

        /// <summary>The flash block browser's rows (buttonEdit1_ButtonClick), the file read again.</summary>
        public static List<FlashBlockRow> FlashBlocks(string file)
        {
            var header = new T8Header();
            header.init(file);
            var rows = new List<FlashBlockRow>();
            foreach (FlashBlock fb in header.FlashBlocks)
            {
                fb.DecodeBlock(out string vin, out string ecu, out string itf, out string secret);
                rows.Add(new FlashBlockRow(fb.BlockNumber, fb.BlockType.ToString(), fb.BlockAddress.ToString("X8"), vin, ecu, itf, secret));
            }
            return rows;
        }

        /// <summary>
        /// Form1.DetermineCarInfoBySWVersion: the software version's last part "NNx" gives the engine and model year, its first
        /// part the market and drive. A last part that isn't "NNx" with a number gives nothing at all, as in T8Suite.
        /// </summary>
        public static string CarInfoBySoftwareVersion(string swversion)
        {
            string retval = string.Empty;
            try
            {
                string[] ids = swversion.Split('_');
                string engineID = ids[^1];
                if (engineID.Length == 3)
                {
                    int engineTypeID = Convert.ToInt32(engineID.Substring(0, 2));
                    char engineMY = engineID[2];
                    switch (engineTypeID)
                    {
                        case 80:
                            retval = "B207E" + engineMY switch
                            {
                                'b' => " MY2003",
                                'c' or 'd' or 'e' => " MY2004/2005",
                                'f' or 'h' => " MY2007/2008",
                                'g' => " MY2007/2008 Biopower",
                                _ => "",
                            };
                            break;
                        case 81:
                            retval = engineMY == 't' ? "B207M/F MY2011" : "B207L" + engineMY switch
                            {
                                'b' => " MY2003",
                                'c' or 'd' or 'e' => " MY2004/2005",
                                'f' or 'h' => " MY2005",
                                'i' => " MY2006",
                                'j' => " MY2007",
                                'l' => " MY2008 Biopower",
                                'm' => " MY2009",
                                _ => "",
                            };
                            break;
                        case 82:
                            retval = "B207R" + engineMY switch
                            {
                                'b' => " MY2003",
                                'c' or 'd' or 'e' => " MY2004/2005",
                                'f' or 'h' => " MY2005",
                                'n' => " MY2006",
                                's' => " MY2007",
                                'r' => " MY2007/2008",
                                'x' or 'v' => " MY2009",
                                '6' or '8' or 'z' => " MY2010",
                                _ => "",
                            };
                            break;
                        case 83:
                            retval = "Z20NET" + engineMY switch
                            {
                                'e' => " MY2003",
                                'f' => " MY2004",
                                'g' => " MY2005",
                                'h' => " MY2006",
                                'i' => " MY2008",
                                _ => "",
                            };
                            break;
                        case 85:
                            retval = engineMY == 'b' ? "B207S MY2011" : "B207R" + engineMY switch
                            {
                                'd' or 'f' => " MY2011",
                                _ => "",
                            };
                            break;
                    }
                }
                string swLabel = ids[0];
                if (swLabel.Length == 4)
                {
                    if (swLabel.StartsWith("FA")) retval += " MY03-06 Gasoline / front wheel drive";
                    if (swLabel.StartsWith("FC")) retval += " MY07-11 Gasoline / front wheel drive";
                    if (swLabel.StartsWith("FD")) retval += " MY07-10 BioPower / front wheel drive";
                    if (swLabel.StartsWith("FE")) retval += " MY09-11 Gasoline / all wheel drive";
                    if (swLabel.StartsWith("FF")) retval += " MY10 BioPower AWD or MY11 Gasoline/BioPower FWD/AWD";
                }
            }
            catch (Exception E)
            {
                logger.Debug(E);
            }
            return retval;
        }
    }
}
