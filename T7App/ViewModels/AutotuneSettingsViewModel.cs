using CommonSuite;
using CommunityToolkit.Mvvm.ComponentModel;

namespace T7App.ViewModels;

/// <summary>frmAutotuneSettings "Autotune settings...": the options T7Suite used (its disabled ones and the ping sound aren't ported).</summary>
public partial class AutotuneSettingsViewModel : ObservableObject
{
    [ObservableProperty] private decimal? _cellStableTime;
    [ObservableProperty] private decimal? _correctionPercentage;
    [ObservableProperty] private decimal? _acceptableTargetError;
    [ObservableProperty] private decimal? _maximumAdjustment;
    [ObservableProperty] private bool _autoUpdateFuelMap;
    [ObservableProperty] private bool _disableClosedLoopOnStartAutotune;
    [ObservableProperty] private string _autoTuneFuelMap;

    public string[] FuelMaps { get; } = ["BFuelCal.Map", "BFuelCal.E85Map", "BFuelCal.StartMap"];

    public AutotuneSettingsViewModel(AppSettings s)
    {
        _cellStableTime = System.Math.Clamp(s.CellStableTime_ms, 100, 10000);
        _correctionPercentage = System.Math.Clamp(s.CorrectionPercentage, 1, 100);
        _acceptableTargetError = System.Math.Clamp(s.AcceptableTargetErrorPercentage, 1, 10);
        _maximumAdjustment = System.Math.Clamp(s.MaximumAdjustmentPerCyclePercentage, 1, 20);
        _autoUpdateFuelMap = s.AutoUpdateFuelMap;
        _disableClosedLoopOnStartAutotune = s.DisableClosedLoopOnStartAutotune;
        _autoTuneFuelMap = System.Array.Exists(FuelMaps, m => m == s.AutoTuneFuelMap) ? s.AutoTuneFuelMap : FuelMaps[0];
    }

    /// <summary>Ok: the values back into AppSettings (each setter saves); an emptied box gets T7Suite's default.</summary>
    public void Apply(AppSettings s)
    {
        s.CellStableTime_ms = (int)(CellStableTime ?? 1000);
        s.CorrectionPercentage = (int)(CorrectionPercentage ?? 50);
        s.AcceptableTargetErrorPercentage = (int)(AcceptableTargetError ?? 2);
        s.MaximumAdjustmentPerCyclePercentage = (int)(MaximumAdjustment ?? 10);
        s.AutoUpdateFuelMap = AutoUpdateFuelMap;
        s.DisableClosedLoopOnStartAutotune = DisableClosedLoopOnStartAutotune;
        s.AutoTuneFuelMap = AutoTuneFuelMap;
    }

    /// <summary>A copy to edit, so a cancelled dialog leaves the values kept so far.</summary>
    public AutotuneSettingsViewModel Clone() => (AutotuneSettingsViewModel)MemberwiseClone();
}
