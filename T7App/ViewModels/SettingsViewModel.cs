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

    // wideband and AFR maps (frmSettings' wideband options, frmWidebandConfig)
    [ObservableProperty] private bool _autoCreateAFRMaps;
    [ObservableProperty] private bool _measureAFRInLambda;
    [ObservableProperty] private bool _useWidebandLambda;
    [ObservableProperty] private string _wideBandSymbol;
    [ObservableProperty] private decimal? _widebandLowVoltage;
    [ObservableProperty] private decimal? _widebandHighVoltage;
    [ObservableProperty] private decimal? _widebandLowAFR;
    [ObservableProperty] private decimal? _widebandHighAFR;

    // the two wideband sources exclude each other
    partial void OnUseWidebandLambdaChanged(bool value) { if (value) UseDigitalWidebandLambda = false; }

    public string[] WidebandSymbols { get; } = ["DisplProt.AD_Scanner", "DisplProt.LambdaScanner"];

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
        if (s.UseWidebandLambda) UseDigitalWidebandLambda = false;
        PropertyChanged += (_, e) => { if (e.PropertyName == nameof(UseDigitalWidebandLambda) && UseDigitalWidebandLambda) UseWidebandLambda = false; };
        _standardFill = System.Math.Clamp(s.StandardFill, 0, 2);
        _writeTimestampInBinary = s.WriteTimestampInBinary;
        _autoFixFooter = s.AutoFixFooter;
        _enableCanLog = s.EnableCanLog;
        _autoCreateAFRMaps = s.AutoCreateAFRMaps;
        _measureAFRInLambda = s.MeasureAFRInLambda;
        _useWidebandLambda = s.UseWidebandLambda;
        _wideBandSymbol = System.Array.Exists(WidebandSymbols, w => w == s.WideBandSymbol) ? s.WideBandSymbol : WidebandSymbols[0];
        _widebandLowVoltage = (decimal)s.WidebandLowVoltage / 1000;
        _widebandHighVoltage = (decimal)s.WidebandHighVoltage / 1000;
        _widebandLowAFR = (decimal)s.WidebandLowAFR / 1000;
        _widebandHighAFR = (decimal)s.WidebandHighAFR / 1000;
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
        s.MeasureAFRInLambda = MeasureAFRInLambda;
        s.UseWidebandLambda = UseWidebandLambda;
        s.WideBandSymbol = WideBandSymbol;
        s.WidebandLowVoltage = (double)(WidebandLowVoltage ?? 0) * 1000;
        s.WidebandHighVoltage = (double)(WidebandHighVoltage ?? 5) * 1000;
        s.WidebandLowAFR = (double)(WidebandLowAFR ?? 7.39m) * 1000;
        s.WidebandHighAFR = (double)(WidebandHighAFR ?? 22.3m) * 1000;
        s.AutoTuneFuelMap = AutoTuneFuelMap;
        s.CellStableTime_ms = (int)(CellStableTime ?? 1000);
        s.CorrectionPercentage = (int)(CorrectionPercentage ?? 50);
        s.AcceptableTargetErrorPercentage = (int)(AcceptableTargetError ?? 2);
        s.MaximumAdjustmentPerCyclePercentage = (int)(MaximumAdjustment ?? 10);
        s.AutoUpdateFuelMap = AutoUpdateFuelMap;
        s.DisableClosedLoopOnStartAutotune = DisableClosedLoopOnStartAutotune;
    }
}
