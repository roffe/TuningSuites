using System;
using System.Collections.Generic;
using System.Linq;
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

public sealed record OptionGroup(string Name, IReadOnlyList<OptionFlag> Flags);

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
        Groups =
        [
            new("Car specifics", [F("Automatic transmission", nameof(props.AutomaticTransmission)), F("Heatplates", nameof(props.Heatedplates)),
                F("ETS/TCS", nameof(props.ETS)), F("Normally aspirated engine", nameof(props.Normalasperatedengine), extended)]),
            new("ECU specifics", [F("VSS enabled", nameof(props.VSSactive), vss), F("RAM locked", nameof(props.RAMlocked)),
                F("Tank pressure diagnostics", nameof(props.Tank_diagnosticsactive), vss), F("Trionic 5.5", nameof(props.IsTrionic55), false)]),
            new("Enrichment", [F("Enrichment after start", nameof(props.Afterstartenrichment)), F("Enrichment during start", nameof(props.Enrichmentduringstart)),
                F("WOT enrichment", nameof(props.WOTenrichment)), F("Acceleration enrichment", nameof(props.Accelerationsenrichment)),
                F("Deceleration enleanment", nameof(props.Decelerationsenleanment))]),
            new("Fuelling", [F("Constant injection (E51)", nameof(props.ConstantinjectiontimeE51)), F("Fuelcut in engine brake", nameof(props.Fuelcut)),
                F("Load control", nameof(props.Loadcontrol)), F("No fuelcut in R12", nameof(props.NofuelcutR12), extended)]),
            new("Idle", [F("Idle control", nameof(props.Idlecontrol)), F("Constant injection time", nameof(props.Constantinjtimeduringidle)),
                F("Use idle injection map", nameof(props.Usesseparateinjmapduringidle)), F("Higher idle during start", nameof(props.Higheridleduringstart)),
                F("Load buffering during idle", nameof(props.Loadbufferduringidle), extended)]),
            new("Adaption", [F("Adaptivity", nameof(props.Adaptivity)), F("Adaptivity with closed throttle", nameof(props.Adaptivitywithclosedthrottle)),
                F("Fuel adjustment during idle", nameof(props.Fueladjustingduringidle)), F("Adaption of idle control", nameof(props.Adaptionofidlecontrol)),
                F("Global adaption", nameof(props.Globaladaption))]),
            new("Misc", [F("Temperature correction", nameof(props.Temperaturecompensation)), F("Temp. corr. in closed loop", nameof(props.Tempcompwithactivelambdacontrol)),
                F("Purge control", nameof(props.Purge)), F("Boost control", nameof(props.APCcontrol)),
                F("Fixed idle ignition gear 1&2", nameof(props.Constidleignangleduringgearoneandtwo), extended), F("Airpump control", nameof(props.Airpumpcontrol), extended),
                F("Knock detection OFF", nameof(props.Knockregulatingdisabled), extended), F("Purge valve MY94", nameof(props.PurgevalveMY94), extended)]),
            new("Lambda control", [F("Lambda control", nameof(props.Lambdacontrol)), F("Lambda control on transients", nameof(props.Lambdacontrolduringtransients)),
                F("Correction for TPS opening", nameof(props.Factortolambdawhenthrottleopening)), F("Correction for engaging A/C", nameof(props.FactortolambdawhenACisengaged)),
                F("Lambda control during idle", nameof(props.Lambdacontrolduringidle)),
                // T5.2 has no second sensor: shown as the file has it, not written (T5Suite's OK cleared it)
                F("Enable second lambda sensor", nameof(props.SecondO2Enable), props.IsTrionic55)]),
            new("Advanced", [F("Interpolation of delay", nameof(props.Interpolationofdelay)),
                F("Throttle acc/ret adjust MY95", nameof(props.ThrottleAccRetadjustsimultMY95)), F("Constant angle", nameof(props.Constantangle), extended)]),
        ];
    }

    public IReadOnlyList<OptionGroup> Groups { get; }

    public OptionGroup CarGroup => Groups[0];
    public OptionGroup EcuGroup => Groups[1];
    public IReadOnlyList<OptionGroup> FlagGroups => Groups.Skip(2).Take(Groups.Count - 3).ToList();
    public OptionGroup AdvancedGroup => Groups[^1];

    public static InjectorType[] Injectors { get; } = Enum.GetValues<InjectorType>();
    public static MapSensorType[] MapSensors { get; } = Enum.GetValues<MapSensorType>();
    // by name: T5Suite's list was one off against the enum and lacked the last two
    public static TurboType[] Turbos { get; } = Enum.GetValues<TurboType>();
    public static TuningStage[] Stages { get; } = Enum.GetValues<TuningStage>();

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
