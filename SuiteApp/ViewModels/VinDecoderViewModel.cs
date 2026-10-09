using CommonSuite;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SuiteApp.ViewModels;

/// <summary>A VIN decoded as frmDecodeVIN showed it: the car, its turbo and whether the check digit matches.</summary>
public sealed class VinDecoding(string vin)
{
    private readonly string m_vin = vin.Trim();

    public VINCarInfo Car { get; } = VINDecoder.DecodeVINNumber(vin.Trim());

    public string Turbo => Car.TurboModel.ToString().Replace('_', '-');

    public string Checksum =>
        Car.CalculatedChecksum == '*' || m_vin.Length < 9 ? "Not verified"
        : Car.CalculatedChecksum == m_vin[8] ? "Valid" : $"WRONG! Expected: {Car.CalculatedChecksum} but found: {m_vin[8]}";
}

/// <summary>frmDecodeVIN ("VIN decoder"): the bin's VIN decoded, or one typed in and decoded with Enter or Decode.</summary>
public partial class VinDecoderViewModel(string vin) : ObservableObject
{
    [ObservableProperty] private string _vin = vin;
    [ObservableProperty] private VinDecoding _decoded = new(vin);

    public void Decode()
    {
        Vin = Vin.ToUpperInvariant();
        Decoded = new VinDecoding(Vin);
    }
}
