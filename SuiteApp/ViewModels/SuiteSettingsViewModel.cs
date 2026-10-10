using System;
using CommonSuite;
using CommunityToolkit.Mvvm.ComponentModel;
using TrionicCANLib.API;

namespace SuiteApp.ViewModels;

/// <summary>
/// frmSettings, the settings both suites have that do something here (of the docking / window size options only Hide symbol
/// window) and the connection; each suite's dialog adds its own.
/// </summary>
public partial class SuiteSettingsViewModel : ObservableObject
{
    [ObservableProperty] private bool _showRedWhite;
    [ObservableProperty] private bool _showGraphs;
    [ObservableProperty] private bool _disableMapviewerColors;
    [ObservableProperty] private bool _autoLoadLastFile;
    [ObservableProperty] private int _defaultViewType;
    [ObservableProperty] private bool _synchronizeMapviewers;
    [ObservableProperty] private bool _autoChecksum;
    [ObservableProperty] private bool _showAddressesInHex;
    [ObservableProperty] private bool _requestProjectNotes;
    [ObservableProperty] private bool _hideSymbolTable;
    [ObservableProperty] private string _projectFolder;

    public string[] ViewTypes { get; } = ["Hexadecimal view", "Decimal view", "Easy view"];

    // realtime settings: the connection, the same for every suite
    [ObservableProperty] private string _adapterType = "";
    [ObservableProperty] private string? _adapter;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAdapters))]
    private string[] _adapters = [];

    public bool HasAdapters => Adapters.Length > 0;
    [ObservableProperty] private bool _adapterNeedsBaudrate;
    [ObservableProperty] private int _baudrate;
    [ObservableProperty] private bool _onlyPBus;
    [ObservableProperty] private bool _autoUpdateSRAMViewers;
    [ObservableProperty] private decimal? _autoUpdateInterval;

    // "Use wideband O2 on com port", device and port
    [ObservableProperty] private bool _useDigitalWidebandLambda;
    [ObservableProperty] private string _widebandDevice;
    [ObservableProperty] private string _wbPort;

    // the wideband input through an ECU symbol (frmSettings' wideband options, frmWidebandConfig); not shown by T8
    [ObservableProperty] private bool _measureAFRInLambda;
    [ObservableProperty] private bool _useWidebandLambda;
    [ObservableProperty] private string _wideBandSymbol;
    [ObservableProperty] private decimal? _widebandLowVoltage;
    [ObservableProperty] private decimal? _widebandHighVoltage;
    [ObservableProperty] private decimal? _widebandLowAFR;
    [ObservableProperty] private decimal? _widebandHighAFR;

    // the two wideband sources exclude each other
    partial void OnUseWidebandLambdaChanged(bool value) { if (value) UseDigitalWidebandLambda = false; }

    /// <summary>The symbols the wideband can come through (T7: the scanner inputs; T5: AD_EGR pin 69, AD_cat pin 70).</summary>
    public virtual string[] WidebandSymbols => ["DisplProt.AD_Scanner", "DisplProt.LambdaScanner"];

    public string[] WidebandDevices { get; } = ["PLX", "LM1", "LC1", "LM2", "ZT2", "AEM", "STAG", "LambdaShield"];
    public string[] SerialPorts { get; } = System.IO.Ports.SerialPort.GetPortNames();

    public string[] AdapterTypes { get; } = CanAdapters.Types;

    // frmComportSettings' speeds, plus the SLCAN ones the flasher offers
    public int[] Baudrates { get; } = [9600, 38400, 115200, 230400, 1000000, 2000000, 3000000];

    partial void OnAdapterTypeChanged(string value)
    {
        CANBusAdapter? type = CanAdapters.FromDescription(value);
        Adapters = type is { } t ? ITrionic.GetAdapterNames(t) ?? [] : [];
        if (Adapter == null || !Array.Exists(Adapters, a => a == Adapter)) Adapter = Adapters.Length > 0 ? Adapters[0] : null;
        AdapterNeedsBaudrate = CanAdapters.NeedsBaudrate(type);
    }

    public SuiteSettingsViewModel(AppSettings s)
    {
        _showRedWhite = s.ShowRedWhite;
        _showGraphs = s.ShowGraphs;
        _disableMapviewerColors = s.DisableMapviewerColors;
        _autoLoadLastFile = s.AutoLoadLastFile;
        // values past Easy (the bar views) show as Easy, like the old combo
        _defaultViewType = System.Math.Min((int)s.DefaultViewType, 2);
        _synchronizeMapviewers = s.SynchronizeMapviewers;
        _autoChecksum = s.AutoChecksum;
        _showAddressesInHex = s.ShowAddressesInHex;
        _requestProjectNotes = s.RequestProjectNotes;
        _hideSymbolTable = s.HideSymbolTable;
        _projectFolder = s.ProjectFolder;
        _adapter = s.Adapter;
        _baudrate = s.Baudrate;
        _onlyPBus = s.OnlyPBus;
        _autoUpdateSRAMViewers = s.AutoUpdateSRAMViewers;
        _autoUpdateInterval = Math.Clamp(s.AutoUpdateInterval, 5, 60);
        _useDigitalWidebandLambda = s.UseDigitalWidebandLambda;
        _widebandDevice = Array.Exists(WidebandDevices, d => d == s.WidebandDevice) ? s.WidebandDevice : "LC1";
        _wbPort = s.WbPort;
        _measureAFRInLambda = s.MeasureAFRInLambda;
        _useWidebandLambda = s.UseWidebandLambda;
        if (s.UseWidebandLambda) _useDigitalWidebandLambda = false;
        _wideBandSymbol = Array.Exists(WidebandSymbols, w => w == s.WideBandSymbol) ? s.WideBandSymbol : WidebandSymbols[0];
        _widebandLowVoltage = (decimal)s.WidebandLowVoltage / 1000;
        _widebandHighVoltage = (decimal)s.WidebandHighVoltage / 1000;
        _widebandLowAFR = (decimal)s.WidebandLowAFR / 1000;
        _widebandHighAFR = (decimal)s.WidebandHighAFR / 1000;
        PropertyChanged += (_, e) => { if (e.PropertyName == nameof(UseDigitalWidebandLambda) && UseDigitalWidebandLambda) UseWidebandLambda = false; };
        // not the field: the setter fills the adapter list
        AdapterType = Array.Exists(AdapterTypes, a => a == s.AdapterType) ? s.AdapterType : AdapterTypes[0];
    }

    /// <summary>OK: every value back into AppSettings (each setter saves).</summary>
    public virtual void Apply(AppSettings s)
    {
        s.ShowRedWhite = ShowRedWhite;
        s.ShowGraphs = ShowGraphs;
        s.DisableMapviewerColors = DisableMapviewerColors;
        s.AutoLoadLastFile = AutoLoadLastFile;
        s.DefaultViewType = (SuiteViewType)DefaultViewType;
        s.SynchronizeMapviewers = SynchronizeMapviewers;
        s.AutoChecksum = AutoChecksum;
        s.ShowAddressesInHex = ShowAddressesInHex;
        s.RequestProjectNotes = RequestProjectNotes;
        s.HideSymbolTable = HideSymbolTable;
        s.AdapterType = AdapterType;
        s.Adapter = Adapter ?? "";
        s.Baudrate = Baudrate;
        s.OnlyPBus = OnlyPBus;
        s.AutoUpdateSRAMViewers = AutoUpdateSRAMViewers;
        s.AutoUpdateInterval = (int)(AutoUpdateInterval ?? 20);
        s.UseDigitalWidebandLambda = UseDigitalWidebandLambda;
        s.WidebandDevice = WidebandDevice;
        s.WbPort = WbPort ?? "";
        s.MeasureAFRInLambda = MeasureAFRInLambda;
        s.UseWidebandLambda = UseWidebandLambda;
        s.WideBandSymbol = WideBandSymbol;
        s.WidebandLowVoltage = (double)(WidebandLowVoltage ?? 0) * 1000;
        s.WidebandHighVoltage = (double)(WidebandHighVoltage ?? 5) * 1000;
        s.WidebandLowAFR = (double)(WidebandLowAFR ?? 7.39m) * 1000;
        s.WidebandHighAFR = (double)(WidebandHighAFR ?? 22.3m) * 1000;
        // an empty folder fell back to <program>\Projects; the program folder isn't writable on Linux, so the default instead
        s.ProjectFolder = string.IsNullOrWhiteSpace(ProjectFolder)
            ? System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), "TxSuite", "Projects")
            : ProjectFolder;
    }
}
