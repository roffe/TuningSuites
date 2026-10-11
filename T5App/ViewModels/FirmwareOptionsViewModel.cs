using System;
using System.Collections.Generic;
using System.Reflection;
using CommonSuite;
using CommunityToolkit.Mvvm.ComponentModel;
using Trionic5Tools;

namespace T5App.ViewModels;

/// <summary>A Pgm_mod flag (or another switch) of the options window, bound to its Trionic5Properties property.</summary>
public sealed class OptionFlag(Trionic5Properties props, string caption, string property, bool enabled = true)
{
    private readonly PropertyInfo m_prop = typeof(Trionic5Properties).GetProperty(property)!;

    public string Caption { get; } = caption;
    public bool IsEnabled { get; } = enabled;

    public bool IsChecked
    {
        get => (bool)m_prop.GetValue(props)!;
        set => m_prop.SetValue(props, value);
    }
}

/// <summary>
/// "Trionic options (firmware)": frmEasyFirmwareSettings' groups over a copy of the file's Trionic5Properties, with the
/// grid-only fields in Advanced. Ok writes the changed ones (Trionic5File.SetTrionicOptions) and updates the checksum.
/// </summary>
public partial class FirmwareOptionsViewModel : ObservableObject
{
    public Trionic5Properties Properties { get; }

    public FirmwareOptionsViewModel(Trionic5Properties props)
    {
        Properties = props;
        bool extended = props.ExtendedProgramModeOptions, vss = props.HasVSSOptions;
        OptionFlag F(string caption, string property, bool enabled = true) => new(props, caption, property, enabled);
        // each group in frmEasyFirmwareSettings' order: row by row, the left column first
        Car = [F("Automatic transmission", nameof(props.AutomaticTransmission)), F("ETS/TCS", nameof(props.ETS)),
            F("Heatplates", nameof(props.Heatedplates)), F("Normally aspirated engine", nameof(props.Normalasperatedengine), extended)];
        VssEnabled = F("VSS enabled", nameof(props.VSSactive), vss);
        Trionic55 = F("Trionic 5.5", nameof(props.IsTrionic55), false);
        RamLocked = F("RAM locked", nameof(props.RAMlocked));
        TankDiagnostics = F("Tank pressure diagnostics", nameof(props.Tank_diagnosticsactive), vss);
        Fuelling = [F("Constant injection (E51)", nameof(props.ConstantinjectiontimeE51)), F("Load control", nameof(props.Loadcontrol)),
            F("Fuelcut in engine brake", nameof(props.Fuelcut)), F("No fuelcut in R12", nameof(props.NofuelcutR12), extended)];
        Idle = [F("Constant injection time", nameof(props.Constantinjtimeduringidle)), F("Load buffering during idle", nameof(props.Loadbufferduringidle), extended),
            F("Higher idle during start", nameof(props.Higheridleduringstart)), F("Use idle injection map", nameof(props.Usesseparateinjmapduringidle)),
            F("Idle control", nameof(props.Idlecontrol))];
        Misc = [F("Airpump control", nameof(props.Airpumpcontrol), extended), F("Knock detection OFF", nameof(props.Knockregulatingdisabled), extended),
            F("Purge control", nameof(props.Purge)), F("Temperature correction", nameof(props.Temperaturecompensation)),
            F("Boost control", nameof(props.APCcontrol)), F("Fixed idle ignition gear 1&2", nameof(props.Constidleignangleduringgearoneandtwo), extended),
            F("Purge valve MY94", nameof(props.PurgevalveMY94), extended), F("Temp. corr. in closed loop", nameof(props.Tempcompwithactivelambdacontrol))];
        Enrichment = [F("Acceleration enrichment", nameof(props.Accelerationsenrichment)), F("Enrichment during start", nameof(props.Enrichmentduringstart)),
            F("Deceleration enleanment", nameof(props.Decelerationsenleanment)), F("Enrichment after start", nameof(props.Afterstartenrichment)),
            F("WOT enrichment", nameof(props.WOTenrichment))];
        Adaption = [F("Adaption of idle control", nameof(props.Adaptionofidlecontrol)), F("Fuel adjustment during idle", nameof(props.Fueladjustingduringidle)),
            F("Adaptivity", nameof(props.Adaptivity)), F("Global adaption", nameof(props.Globaladaption)),
            F("Adaptivity with closed throttle", nameof(props.Adaptivitywithclosedthrottle))];
        Lambda = [F("Correction for engaging A/C", nameof(props.FactortolambdawhenACisengaged)), F("Lambda control during idle", nameof(props.Lambdacontrolduringidle)),
            F("Correction for TPS opening", nameof(props.Factortolambdawhenthrottleopening)), F("Lambda control on transients", nameof(props.Lambdacontrolduringtransients)),
            F("Lambda control", nameof(props.Lambdacontrol)),
            // T5.2 has no second sensor: shown as the file has it, not written (T5Suite's OK cleared it)
            F("Enable second lambda sensor", nameof(props.SecondO2Enable), props.IsTrionic55)];
        Advanced = [F("Interpolation of delay", nameof(props.Interpolationofdelay)),
            F("Throttle acc/ret adjust MY95", nameof(props.ThrottleAccRetadjustsimultMY95)), F("Constant angle", nameof(props.Constantangle), extended)];
    }

    public IReadOnlyList<OptionFlag> Car { get; }
    public OptionFlag VssEnabled { get; }
    public OptionFlag Trionic55 { get; }
    public OptionFlag RamLocked { get; }
    public OptionFlag TankDiagnostics { get; }
    public IReadOnlyList<OptionFlag> Fuelling { get; }
    public IReadOnlyList<OptionFlag> Idle { get; }
    public IReadOnlyList<OptionFlag> Misc { get; }
    public IReadOnlyList<OptionFlag> Enrichment { get; }
    public IReadOnlyList<OptionFlag> Adaption { get; }
    public IReadOnlyList<OptionFlag> Lambda { get; }
    public IReadOnlyList<OptionFlag> Advanced { get; }

    public static InjectorType[] Injectors { get; } = Enum.GetValues<InjectorType>();
    public static MapSensorType[] MapSensors { get; } = Enum.GetValues<MapSensorType>();
    // by name: T5Suite's list was one off against the enum and lacked the last two
    public static TurboType[] Turbos { get; } = Enum.GetValues<TurboType>();
    public static TuningStage[] Stages { get; } = Enum.GetValues<TuningStage>();

    /// <summary>frmEasyFirmwareSettings' combo captions (GT17 and S400SX371 weren't in its turbo list).</summary>
    public static string Caption(Enum value) => value switch
    {
        InjectorType i => new[] { "Stock", "Green giants", "Siemens 630cc/min", "Siemens 875cc/min", "Siemens 1000cc/min" }[(int)i],
        MapSensorType m => new[] { "2.5 bar sensor", "3.0 bar sensor", "3.5 bar sensor", "4.0 bar sensor", "5.0 bar sensor" }[(int)m],
        TurboType.TD0415T => "TD04-15T",
        TurboType.TD0419T => "TD04-19T",
        TuningStage.StageX => "Stage X",
        TuningStage s and not TuningStage.Stock => $"Stage {(int)s}",
        _ => value.ToString(),
    };

    public string CarModel => Properties.Carmodel;
    public string EngineType => Properties.Enginetype;
    public string CpuSpeed => Properties.CPUspeed;
    public string Dataname => Properties.Dataname;
    public string SoftwareId => Properties.SoftwareID;
    public string SyncTimestamp => Properties.SyncDateTime.ToString("dd/MM/yyyy HH:mm:ss");
    public bool HasVss => Properties.HasVSSOptions;

    /// <summary>The partnumber and VSS code keep their footer field's length.</summary>
    public int PartnumberLength { get; init; } = 7;
    public int VssCodeLength { get; init; } = 5;

    public string Partnumber
    {
        get => Properties.Partnumber;
        set => Properties.Partnumber = value;
    }

    public string VssCode
    {
        get => Properties.VSSCode;
        set => Properties.VSSCode = value;
    }

    public InjectorType Injector
    {
        get => Properties.InjectorType;
        set => Properties.InjectorType = value;
    }

    public MapSensorType MapSensor
    {
        get => Properties.MapSensorType;
        set => Properties.MapSensorType = value;
    }

    public TurboType Turbo
    {
        get => Properties.TurboType;
        set => Properties.TurboType = value;
    }

    public TuningStage Stage
    {
        get => Properties.TuningStage;
        set => Properties.TuningStage = value;
    }

    /// <summary>The hardcoded RPM limit (grid only in T5Suite); only when the code pattern is in the file.</summary>
    public bool HasRpmLimit { get; init; }

    public decimal? RpmLimit
    {
        get => Properties.HardcodedRPMLimit;
        set => Properties.HardcodedRPMLimit = (int)(value ?? Properties.HardcodedRPMLimit);
    }
}
