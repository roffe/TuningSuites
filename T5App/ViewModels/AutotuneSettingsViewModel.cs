using CommonSuite;
using CommunityToolkit.Mvvm.ComponentModel;
using Trionic5Tools;

namespace T5App.ViewModels;

/// <summary>frmAutotuneSettings "Autotune settings...": the fuel autotune's (shared names) and the ignition autotune's (T5Suite's own).</summary>
public partial class AutotuneSettingsViewModel : ObservableObject
{
    [ObservableProperty] private decimal? _cellStableTime;
    [ObservableProperty] private decimal? _correctionPercentage;
    [ObservableProperty] private decimal? _acceptableTargetError;
    [ObservableProperty] private decimal? _maximumAdjustment;
    [ObservableProperty] private decimal? _enrichmentFilter;
    [ObservableProperty] private bool _autoUpdateFuelMap;
    [ObservableProperty] private bool _discardFuelcut;
    [ObservableProperty] private bool _discardClosedThrottle;
    [ObservableProperty] private bool _disableClosedLoop;
    [ObservableProperty] private bool _resetFuelTrims;
    [ObservableProperty] private bool _allowIdleAutoTune;
    [ObservableProperty] private bool _alwaysCreateAfrMaps;
    [ObservableProperty] private decimal? _ignitionCellStableTime;
    [ObservableProperty] private decimal? _minimumEngineSpeed;
    [ObservableProperty] private decimal? _ignitionAdvancePerCycle;
    [ObservableProperty] private decimal? _retardFirstKnock;
    [ObservableProperty] private decimal? _retardFurtherKnocks;
    [ObservableProperty] private decimal? _globalMaximumAdvance;
    [ObservableProperty] private decimal? _maximumAdvancePerSession;
    [ObservableProperty] private bool _capIgnitionMap;

    public AutotuneSettingsViewModel(AppSettings s, T5AppSettings t5)
    {
        _cellStableTime = s.CellStableTime_ms;
        _correctionPercentage = s.CorrectionPercentage;
        _acceptableTargetError = s.AcceptableTargetErrorPercentage;
        _maximumAdjustment = s.MaximumAdjustmentPerCyclePercentage;
        _enrichmentFilter = s.EnrichmentFilter;
        _autoUpdateFuelMap = s.AutoUpdateFuelMap;
        _discardFuelcut = s.DiscardFuelcutMeasurements;
        _discardClosedThrottle = s.DiscardClosedThrottleMeasurements;
        _disableClosedLoop = s.DisableClosedLoopOnStartAutotune;
        _allowIdleAutoTune = s.AllowIdleAutoTune;
        _resetFuelTrims = t5.ResetFuelTrims;
        _alwaysCreateAfrMaps = t5.AlwaysCreateAFRMaps;
        _ignitionCellStableTime = t5.IgnitionCellStableTime_ms;
        _minimumEngineSpeed = t5.MinimumEngineSpeedForIgnitionTuning;
        _ignitionAdvancePerCycle = (decimal)t5.IgnitionAdvancePerCycle;
        _retardFirstKnock = (decimal)t5.IgnitionRetardFirstKnock;
        _retardFurtherKnocks = (decimal)t5.IgnitionRetardFurtherKnocks;
        _globalMaximumAdvance = (decimal)t5.GlobalMaximumIgnitionAdvance;
        _maximumAdvancePerSession = (decimal)t5.MaximumIgnitionAdvancePerSession;
        _capIgnitionMap = t5.CapIgnitionMap;
    }

    /// <summary>Ok: every value back (each setter saves); an emptied box gets T5Suite's default.</summary>
    public void Apply(AppSettings s, T5AppSettings t5)
    {
        s.CellStableTime_ms = (int)(CellStableTime ?? 1000);
        s.CorrectionPercentage = (int)(CorrectionPercentage ?? 50);
        s.AcceptableTargetErrorPercentage = (int)(AcceptableTargetError ?? 2);
        s.MaximumAdjustmentPerCyclePercentage = (int)(MaximumAdjustment ?? 10);
        s.EnrichmentFilter = (int)(EnrichmentFilter ?? 3);
        s.AutoUpdateFuelMap = AutoUpdateFuelMap;
        s.DiscardFuelcutMeasurements = DiscardFuelcut;
        s.DiscardClosedThrottleMeasurements = DiscardClosedThrottle;
        s.DisableClosedLoopOnStartAutotune = DisableClosedLoop;
        s.AllowIdleAutoTune = AllowIdleAutoTune;
        t5.ResetFuelTrims = ResetFuelTrims;
        t5.AlwaysCreateAFRMaps = AlwaysCreateAfrMaps;
        t5.IgnitionCellStableTime_ms = (int)(IgnitionCellStableTime ?? 500);
        t5.MinimumEngineSpeedForIgnitionTuning = (int)(MinimumEngineSpeed ?? 1200);
        t5.IgnitionAdvancePerCycle = (double)(IgnitionAdvancePerCycle ?? 0.1m);
        t5.IgnitionRetardFirstKnock = (double)(RetardFirstKnock ?? 1.0m);
        t5.IgnitionRetardFurtherKnocks = (double)(RetardFurtherKnocks ?? 0.5m);
        t5.GlobalMaximumIgnitionAdvance = (double)(GlobalMaximumAdvance ?? 35m);
        t5.MaximumIgnitionAdvancePerSession = (double)(MaximumAdvancePerSession ?? 2m);
        t5.CapIgnitionMap = CapIgnitionMap;
    }
}
