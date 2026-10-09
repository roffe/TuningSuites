using CommonSuite;
using T7;

namespace T7App.ViewModels;

/// <summary>frmFirmwareInformation, read-only for now; the VIN decoder of frmDecodeVIN is shown inline.</summary>
public class FirmwareInfoViewModel(FirmwareInfo info)
{
    public FirmwareInfo Info { get; } = info;

    public string ProgrammingDate => Info.ProgrammingDate?.ToString("g") ?? "not stamped";

    public VINCarInfo Vin { get; } = VINDecoder.DecodeVINNumber(info.ChassisID.Trim());

    public string VinChecksum
    {
        get
        {
            string vin = Info.ChassisID.Trim();
            if (Vin.CalculatedChecksum == '*' || vin.Length < 9) return "Not verified";
            return Vin.CalculatedChecksum == vin[8] ? "Valid" : $"WRONG! Expected: {Vin.CalculatedChecksum} but found: {vin[8]}";
        }
    }

    public string Turbo => Vin.TurboModel.ToString().Replace('_', '-');
}
