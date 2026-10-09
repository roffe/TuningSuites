using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using T7;

namespace T7App.ViewModels;

/// <summary>frmEspSelection: the ESP calibration; Ok needs a choice (T7Suite wrote 0x00 when an unknown value was kept).</summary>
public partial class EspViewModel(byte current) : ObservableObject
{
    public string[] Names { get; } = FirmwareTools.EspCalibrations.Select(c => c.name).ToArray();

    [ObservableProperty]
    private int _selected = Array.FindIndex(FirmwareTools.EspCalibrations, c => c.value == current);

    public byte? Value => Selected >= 0 ? FirmwareTools.EspCalibrations[Selected].value : null;
}

/// <summary>frmTcmLimit ("TCM Limiter Modification"): MOD v1 or MOD v2, never both.</summary>
public partial class TcmViewModel : ObservableObject
{
    public TcmLimit Before { get; }
    public string[] Thresholds { get; } = FirmwareTools.TcmThresholds.Select(t => t.name).ToArray();
    public string[] Gears { get; } = FirmwareTools.TcmGears.Select(t => t.name).ToArray();
    public string[] GearLimits { get; } = FirmwareTools.TcmGearLimits.Select(t => t.name).ToArray();

    [ObservableProperty] private bool _thresholdMod;
    [ObservableProperty] private int _threshold;
    [ObservableProperty] private bool _gearMod;
    [ObservableProperty] private int _gear;
    [ObservableProperty] private int _gearLimit;

    public TcmViewModel(TcmLimit before)
    {
        Before = before;
        _thresholdMod = before.ThresholdMod;
        _threshold = Math.Max(0, Array.FindIndex(FirmwareTools.TcmThresholds, t => t.value == before.TorqueLimit));
        _gearMod = before.GearMod;
        _gear = Math.Max(0, Array.FindIndex(FirmwareTools.TcmGears, t => t.value == before.Gear));
        // an unknown limit shows 300 Nm, as the dialog's default
        int limit = Array.FindIndex(FirmwareTools.TcmGearLimits, t => t.value == before.GearLimit);
        _gearLimit = limit >= 0 ? limit : 2;
    }

    partial void OnThresholdModChanged(bool value) { if (value) GearMod = false; }
    partial void OnGearModChanged(bool value) { if (value) ThresholdMod = false; }

    public TcmLimit After => new(ThresholdMod, FirmwareTools.TcmThresholds[Threshold].value, GearMod,
        FirmwareTools.TcmGears[Gear].value, FirmwareTools.TcmGearLimits[GearLimit].value);
}
