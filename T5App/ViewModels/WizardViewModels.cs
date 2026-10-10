using System;
using System.Collections.Generic;
using System.Linq;
using CommonSuite;
using CommunityToolkit.Mvvm.ComponentModel;
using Trionic5Tools;

namespace T5App.ViewModels;

/// <summary>
/// frmTuningWizard ("Tune me up ®"): stage 1-3, or stage 4 and higher with the free tuning settings. ponytail: one torque and
/// boost range for every sensor / injector / turbo (T5Suite's UpdateMaxima narrowed them per combination).
/// </summary>
public partial class TuneMeUpViewModel : ObservableObject
{
    public TuneMeUpViewModel(T5Tuning.TuneDefaults d)
    {
        _stage = d.Stage;
        _mapSensor = d.MapSensor;
        _injectors = d.Injectors;
        _turbo = d.Turbo;
        _valve = d.Valve;
        _rpmLimit = Math.Clamp(d.RpmLimit, 6000, 8500);
        _knockTime = Math.Clamp(d.KnockTime, 1000, 20000);
    }

    public static IReadOnlyList<string> Stages { get; } =
        ["Stage 1 (+30 bhp, +40Nm)", "Stage 2 (+50 bhp, +60Nm)", "Stage 3 (+80 bhp, +80Nm)", "Stage 4 and higher"];

    public static MapSensorType[] MapSensors { get; } = Enum.GetValues<MapSensorType>();
    public static InjectorType[] InjectorTypes { get; } = Enum.GetValues<InjectorType>();
    public static TurboType[] Turbos { get; } = Enum.GetValues<TurboType>();
    public static BPCType[] Valves { get; } = Enum.GetValues<BPCType>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFreeTune), nameof(StageIndex))]
    private int _stage;

    /// <summary>The stage list's index (stage − 1).</summary>
    public int StageIndex
    {
        get => Stage - 1;
        set => Stage = value + 1;
    }

    public bool IsFreeTune => Stage == 4;

    [ObservableProperty] private MapSensorType _mapSensor;
    [ObservableProperty] private InjectorType _injectors;
    [ObservableProperty] private TurboType _turbo;
    [ObservableProperty] private BPCType _valve;
    [ObservableProperty] private bool _byTorque = true;
    [ObservableProperty] private decimal? _peakTorque = 400;
    [ObservableProperty] private decimal? _peakBoost = 1.4m;
    [ObservableProperty] private decimal? _rpmLimit;
    [ObservableProperty] private decimal? _knockTime;
}

/// <summary>frmInjectorWizard: the injector type, and the constant, crank factor and battery correction it proposes against the file's.</summary>
public partial class InjectorWizardViewModel : ObservableObject
{
    public InjectorWizardViewModel(T5Tuning.InjectorState current)
    {
        Current = current;
        _type = current.Type;
        _proposed = current;
    }

    public T5Tuning.InjectorState Current { get; }

    public static InjectorType[] Types { get; } = Enum.GetValues<InjectorType>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Rows))]
    private T5Tuning.InjectorState _proposed;

    [ObservableProperty]
    private InjectorType _type;

    partial void OnTypeChanged(InjectorType value) => Proposed = value == Current.Type ? Current : T5Tuning.Propose(Current, value);

    /// <summary>"Current / Proposed" lines: the constant, the crank factor, the battery correction per voltage (15 V first).</summary>
    public IReadOnlyList<string> Rows =>
    [
        $"Injector constant: {Current.Constant} → {Proposed.Constant}",
        $"Crank factor: {Current.CrankFactor:0.00} → {Proposed.CrankFactor:0.00}",
        .. Enumerable.Range(0, Math.Min(Current.BatteryCorrection.Length, Proposed.BatteryCorrection.Length))
            .Select(i => $"Battery correction {15 - i} V: {Current.BatteryCorrection[i]:0.000} ms → {Proposed.BatteryCorrection[i]:0.000} ms"),
    ];
}
