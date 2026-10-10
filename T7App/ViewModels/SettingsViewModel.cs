using CommonSuite;
using CommunityToolkit.Mvvm.ComponentModel;
using SuiteApp.ViewModels;

namespace T7App.ViewModels;

/// <summary>
/// frmSettings: the shared settings and connection plus T7Suite's own (closed loop indicator, timestamp marker, footer fix, CAN log).
/// Wideband and autotune have groups here instead of T7Suite's extra dialogs.
/// </summary>
public partial class SettingsViewModel : SuiteSettingsViewModel
{
    [ObservableProperty] private int _standardFill;
    [ObservableProperty] private bool _writeTimestampInBinary;
    [ObservableProperty] private bool _autoFixFooter;

    // realtime settings beyond the connection
    [ObservableProperty] private bool _enableCanLog;

    // AFR maps (the wideband options are shared)
    [ObservableProperty] private bool _autoCreateAFRMaps;

    // frmAutotuneSettings, the options T7Suite used
    [ObservableProperty] private string _autoTuneFuelMap;
    [ObservableProperty] private decimal? _cellStableTime;
    [ObservableProperty] private decimal? _correctionPercentage;
    [ObservableProperty] private decimal? _acceptableTargetError;
    [ObservableProperty] private decimal? _maximumAdjustment;
    [ObservableProperty] private bool _autoUpdateFuelMap;
    [ObservableProperty] private bool _disableClosedLoopOnStartAutotune;

    public string[] FuelMaps { get; } = ["BFuelCal.Map", "BFuelCal.E85Map", "BFuelCal.StartMap"];

    public string[] ClosedLoopIndicators { get; } = ["No closed loop indicator", "Square closed loop indicator", "Triangle closed loop indicator"];

    public SettingsViewModel(AppSettings s) : base(s)
    {
        _standardFill = System.Math.Clamp(s.StandardFill, 0, 2);
        _writeTimestampInBinary = s.WriteTimestampInBinary;
        _autoFixFooter = s.AutoFixFooter;
        _enableCanLog = s.EnableCanLog;
        _autoCreateAFRMaps = s.AutoCreateAFRMaps;
        _autoTuneFuelMap = System.Array.Exists(FuelMaps, m => m == s.AutoTuneFuelMap) ? s.AutoTuneFuelMap : FuelMaps[0];
        _cellStableTime = System.Math.Clamp(s.CellStableTime_ms, 100, 10000);
        _correctionPercentage = System.Math.Clamp(s.CorrectionPercentage, 1, 100);
        _acceptableTargetError = System.Math.Clamp(s.AcceptableTargetErrorPercentage, 1, 10);
        _maximumAdjustment = System.Math.Clamp(s.MaximumAdjustmentPerCyclePercentage, 1, 20);
        _autoUpdateFuelMap = s.AutoUpdateFuelMap;
        _disableClosedLoopOnStartAutotune = s.DisableClosedLoopOnStartAutotune;
    }

    public override void Apply(AppSettings s)
    {
        base.Apply(s);
        s.StandardFill = StandardFill;
        s.WriteTimestampInBinary = WriteTimestampInBinary;
        s.AutoFixFooter = AutoFixFooter;
        s.EnableCanLog = EnableCanLog;
        s.AutoCreateAFRMaps = AutoCreateAFRMaps;
        s.AutoTuneFuelMap = AutoTuneFuelMap;
        s.CellStableTime_ms = (int)(CellStableTime ?? 1000);
        s.CorrectionPercentage = (int)(CorrectionPercentage ?? 50);
        s.AcceptableTargetErrorPercentage = (int)(AcceptableTargetError ?? 2);
        s.MaximumAdjustmentPerCyclePercentage = (int)(MaximumAdjustment ?? 10);
        s.AutoUpdateFuelMap = AutoUpdateFuelMap;
        s.DisableClosedLoopOnStartAutotune = DisableClosedLoopOnStartAutotune;
    }
}
