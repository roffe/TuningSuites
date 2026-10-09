using CommonSuite;
using CommunityToolkit.Mvvm.ComponentModel;

namespace T7App.ViewModels;

/// <summary>
/// frmSettings, the offline part: the settings that do something in this app (of the docking / window size options only
/// Hide symbol window). Wideband and autotune have groups here instead of T7Suite's extra dialogs.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty] private bool _showRedWhite;
    [ObservableProperty] private bool _showGraphs;
    [ObservableProperty] private bool _disableMapviewerColors;
    [ObservableProperty] private bool _autoLoadLastFile;
    [ObservableProperty] private int _defaultViewType;
    [ObservableProperty] private bool _synchronizeMapviewers;
    [ObservableProperty] private int _standardFill;
    [ObservableProperty] private bool _writeTimestampInBinary;
    [ObservableProperty] private bool _autoChecksum;
    [ObservableProperty] private bool _showAddressesInHex;
    [ObservableProperty] private bool _autoFixFooter;
    [ObservableProperty] private bool _requestProjectNotes;
    [ObservableProperty] private bool _hideSymbolTable;
    [ObservableProperty] private string _projectFolder;

    // realtime settings: the connection
    [ObservableProperty] private string _adapterType;
    [ObservableProperty] private string? _adapter;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAdapters))]
    private string[] _adapters = [];

    public bool HasAdapters => Adapters.Length > 0;
    [ObservableProperty] private bool _adapterNeedsBaudrate;
    [ObservableProperty] private int _baudrate;
    [ObservableProperty] private bool _onlyPBus;
    [ObservableProperty] private bool _enableCanLog;
    [ObservableProperty] private bool _autoUpdateSRAMViewers;
    [ObservableProperty] private decimal? _autoUpdateInterval;

    // wideband and AFR maps (frmSettings' wideband options, frmWidebandConfig)
    [ObservableProperty] private bool _autoCreateAFRMaps;
    [ObservableProperty] private bool _measureAFRInLambda;
    [ObservableProperty] private bool _useWidebandLambda;
    [ObservableProperty] private string _wideBandSymbol;
    [ObservableProperty] private decimal? _widebandLowVoltage;
    [ObservableProperty] private decimal? _widebandHighVoltage;
    [ObservableProperty] private decimal? _widebandLowAFR;
    [ObservableProperty] private decimal? _widebandHighAFR;
    [ObservableProperty] private bool _useDigitalWidebandLambda;
    [ObservableProperty] private string _widebandDevice;
    [ObservableProperty] private string _wbPort;

    // the two wideband sources exclude each other
    partial void OnUseWidebandLambdaChanged(bool value) { if (value) UseDigitalWidebandLambda = false; }
    partial void OnUseDigitalWidebandLambdaChanged(bool value) { if (value) UseWidebandLambda = false; }

    public string[] WidebandSymbols { get; } = ["DisplProt.AD_Scanner", "DisplProt.LambdaScanner"];
    public string[] WidebandDevices { get; } = ["PLX", "LM1", "LC1", "LM2", "ZT2", "AEM", "STAG", "LambdaShield"];
    public string[] SerialPorts { get; } = System.IO.Ports.SerialPort.GetPortNames();

    // frmAutotuneSettings, the options T7Suite used
    [ObservableProperty] private string _autoTuneFuelMap;
    [ObservableProperty] private decimal? _cellStableTime;
    [ObservableProperty] private decimal? _correctionPercentage;
    [ObservableProperty] private decimal? _acceptableTargetError;
    [ObservableProperty] private decimal? _maximumAdjustment;
    [ObservableProperty] private bool _autoUpdateFuelMap;
    [ObservableProperty] private bool _disableClosedLoopOnStartAutotune;

    public string[] FuelMaps { get; } = ["BFuelCal.Map", "BFuelCal.E85Map", "BFuelCal.StartMap"];

    public string[] AdapterTypes { get; } = T7.T7Ecu.AdapterTypes;

    // frmComportSettings' speeds, plus the SLCAN ones the flasher offers
    public int[] Baudrates { get; } = [9600, 38400, 115200, 230400, 1000000, 2000000, 3000000];

    partial void OnAdapterTypeChanged(string value)
    {
        TrionicCANLib.API.CANBusAdapter? type = T7.T7Ecu.AdapterFromDescription(value);
        Adapters = type is { } t ? TrionicCANLib.API.ITrionic.GetAdapterNames(t) ?? [] : [];
        if (Adapter == null || !System.Array.Exists(Adapters, a => a == Adapter)) Adapter = Adapters.Length > 0 ? Adapters[0] : null;
        AdapterNeedsBaudrate = type is TrionicCANLib.API.CANBusAdapter.ELM327 or TrionicCANLib.API.CANBusAdapter.JUST4TRIONIC or TrionicCANLib.API.CANBusAdapter.SLCAN;
    }

    public string[] ViewTypes { get; } = ["Hexadecimal view", "Decimal view", "Easy view"];
    public string[] ClosedLoopIndicators { get; } = ["No closed loop indicator", "Square closed loop indicator", "Triangle closed loop indicator"];

    public SettingsViewModel(AppSettings s)
    {
        _showRedWhite = s.ShowRedWhite;
        _showGraphs = s.ShowGraphs;
        _disableMapviewerColors = s.DisableMapviewerColors;
        _autoLoadLastFile = s.AutoLoadLastFile;
        // values past Easy (the bar views) show as Easy, like the old combo
        _defaultViewType = System.Math.Min((int)s.DefaultViewType, 2);
        _synchronizeMapviewers = s.SynchronizeMapviewers;
        _standardFill = System.Math.Clamp(s.StandardFill, 0, 2);
        _writeTimestampInBinary = s.WriteTimestampInBinary;
        _autoChecksum = s.AutoChecksum;
        _showAddressesInHex = s.ShowAddressesInHex;
        _autoFixFooter = s.AutoFixFooter;
        _requestProjectNotes = s.RequestProjectNotes;
        _hideSymbolTable = s.HideSymbolTable;
        _projectFolder = s.ProjectFolder;
        _adapter = s.Adapter;
        _baudrate = s.Baudrate;
        _onlyPBus = s.OnlyPBus;
        _enableCanLog = s.EnableCanLog;
        _autoUpdateSRAMViewers = s.AutoUpdateSRAMViewers;
        _autoUpdateInterval = System.Math.Clamp(s.AutoUpdateInterval, 5, 60);
        _autoCreateAFRMaps = s.AutoCreateAFRMaps;
        _measureAFRInLambda = s.MeasureAFRInLambda;
        _useWidebandLambda = s.UseWidebandLambda;
        _useDigitalWidebandLambda = s.UseDigitalWidebandLambda && !s.UseWidebandLambda;
        _wideBandSymbol = System.Array.Exists(WidebandSymbols, w => w == s.WideBandSymbol) ? s.WideBandSymbol : WidebandSymbols[0];
        _widebandLowVoltage = (decimal)s.WidebandLowVoltage / 1000;
        _widebandHighVoltage = (decimal)s.WidebandHighVoltage / 1000;
        _widebandLowAFR = (decimal)s.WidebandLowAFR / 1000;
        _widebandHighAFR = (decimal)s.WidebandHighAFR / 1000;
        _widebandDevice = System.Array.Exists(WidebandDevices, d => d == s.WidebandDevice) ? s.WidebandDevice : "LC1";
        _wbPort = s.WbPort;
        _autoTuneFuelMap = System.Array.Exists(FuelMaps, m => m == s.AutoTuneFuelMap) ? s.AutoTuneFuelMap : FuelMaps[0];
        _cellStableTime = System.Math.Clamp(s.CellStableTime_ms, 100, 10000);
        _correctionPercentage = System.Math.Clamp(s.CorrectionPercentage, 1, 100);
        _acceptableTargetError = System.Math.Clamp(s.AcceptableTargetErrorPercentage, 1, 10);
        _maximumAdjustment = System.Math.Clamp(s.MaximumAdjustmentPerCyclePercentage, 1, 20);
        _autoUpdateFuelMap = s.AutoUpdateFuelMap;
        _disableClosedLoopOnStartAutotune = s.DisableClosedLoopOnStartAutotune;
        // not the field: the setter fills the adapter list
        AdapterType = System.Array.Exists(AdapterTypes, a => a == s.AdapterType) ? s.AdapterType : AdapterTypes[0];
    }

    /// <summary>OK: every value back into AppSettings (each setter saves).</summary>
    public void Apply(AppSettings s)
    {
        s.ShowRedWhite = ShowRedWhite;
        s.ShowGraphs = ShowGraphs;
        s.DisableMapviewerColors = DisableMapviewerColors;
        s.AutoLoadLastFile = AutoLoadLastFile;
        s.DefaultViewType = (SuiteViewType)DefaultViewType;
        s.SynchronizeMapviewers = SynchronizeMapviewers;
        s.AutoChecksum = AutoChecksum;
        s.StandardFill = StandardFill;
        s.WriteTimestampInBinary = WriteTimestampInBinary;
        s.ShowAddressesInHex = ShowAddressesInHex;
        s.AutoFixFooter = AutoFixFooter;
        s.RequestProjectNotes = RequestProjectNotes;
        s.HideSymbolTable = HideSymbolTable;
        s.AdapterType = AdapterType;
        s.Adapter = Adapter ?? "";
        s.Baudrate = Baudrate;
        s.OnlyPBus = OnlyPBus;
        s.EnableCanLog = EnableCanLog;
        s.AutoUpdateSRAMViewers = AutoUpdateSRAMViewers;
        s.AutoUpdateInterval = (int)(AutoUpdateInterval ?? 20);
        s.AutoCreateAFRMaps = AutoCreateAFRMaps;
        s.MeasureAFRInLambda = MeasureAFRInLambda;
        s.UseWidebandLambda = UseWidebandLambda;
        s.WideBandSymbol = WideBandSymbol;
        s.WidebandLowVoltage = (double)(WidebandLowVoltage ?? 0) * 1000;
        s.WidebandHighVoltage = (double)(WidebandHighVoltage ?? 5) * 1000;
        s.WidebandLowAFR = (double)(WidebandLowAFR ?? 7.39m) * 1000;
        s.WidebandHighAFR = (double)(WidebandHighAFR ?? 22.3m) * 1000;
        s.UseDigitalWidebandLambda = UseDigitalWidebandLambda;
        s.WidebandDevice = WidebandDevice;
        s.WbPort = WbPort ?? "";
        s.AutoTuneFuelMap = AutoTuneFuelMap;
        s.CellStableTime_ms = (int)(CellStableTime ?? 1000);
        s.CorrectionPercentage = (int)(CorrectionPercentage ?? 50);
        s.AcceptableTargetErrorPercentage = (int)(AcceptableTargetError ?? 2);
        s.MaximumAdjustmentPerCyclePercentage = (int)(MaximumAdjustment ?? 10);
        s.AutoUpdateFuelMap = AutoUpdateFuelMap;
        s.DisableClosedLoopOnStartAutotune = DisableClosedLoopOnStartAutotune;
        // an empty folder fell back to <program>\Projects; the program folder isn't writable on Linux, so the default instead
        s.ProjectFolder = string.IsNullOrWhiteSpace(ProjectFolder)
            ? System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), "TxSuite", "Projects")
            : ProjectFolder;
    }
}
