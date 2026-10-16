using System;
using CommonSuite;
using CommunityToolkit.Mvvm.ComponentModel;
using TrionicCANLib.API;

namespace SuiteApp.ViewModels;

/// <summary>
/// frmSettings, the settings every ECU has that do something here (of the docking / window size options only Hide symbol
/// window) and the connection: Settings → Common; each ECU's tab adds its own.
/// </summary>
public partial class SuiteSettingsViewModel : ObservableObject
{
    [ObservableProperty] private bool _showGraphs;
    [ObservableProperty] private bool _autoLoadLastFile;
    [ObservableProperty] private int _defaultViewType;
    [ObservableProperty] private int _colorBlindMode;
    [ObservableProperty] private decimal? _symbolListFontSize;
    [ObservableProperty] private decimal? _mapCellGap;
    [ObservableProperty] private bool _synchronizeMapviewers;
    [ObservableProperty] private bool _autoChecksum;
    [ObservableProperty] private bool _showAddressesInHex;
    [ObservableProperty] private bool _requestProjectNotes;
    [ObservableProperty] private bool _hideSymbolTable;
    [ObservableProperty] private bool _showMapPreviewPopup;
    [ObservableProperty] private bool _showSymbolDescriptionLine;
    [ObservableProperty] private bool _showRibbon;
    [ObservableProperty] private string _projectFolder;

    public string[] ViewTypes { get; } = ["Hexadecimal view", "Decimal view", "Easy view"];

    public const int MinFontSize = 8, MaxFontSize = 24;

    public const int MaxCellGap = 20;

    /// <summary>txlogger's colour blind modes, in its order and under its names.</summary>
    public string[] ColorBlindModes { get; } = Enum.GetNames<MapControls.ColorBlindMode>();

    // realtime settings: the connection, the same for every ECU
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
    [ObservableProperty] private bool _interpolateLogWorksTimescale;
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

    /// <summary>The symbol when the stored one isn't in the list.</summary>
    protected virtual string DefaultWidebandSymbol => WidebandSymbols[0];

    public string[] WidebandDevices { get; } = ["PLX", "LM1", "LC1", "LM2", "ZT2", "AEM", "STAG", "LambdaShield"];
    public string[] SerialPorts { get; } = System.IO.Ports.SerialPort.GetPortNames();

    public string[] AdapterTypes { get; } = CanAdapters.Types;

    // frmComportSettings' speeds, plus the SLCAN ones the flasher offers
    public int[] Baudrates { get; } = [9600, 38400, 115200, 230400, 1000000, 2000000, 3000000];

    private bool m_opened;

    partial void OnAdapterTypeChanged(string value)
    {
        CANBusAdapter? type = CanAdapters.FromDescription(value);
        Adapters = type is { } t ? ITrionic.GetAdapterNames(t) ?? [] : [];
        // the dialog opens on the saved adapter, an unset (or unplugged) one empty: showing the first one found looked chosen while
        // Connect still said none was; choosing a type picks its first adapter, as T7Suite did
        if (Adapter == null || !Array.Exists(Adapters, a => a == Adapter)) Adapter = m_opened && Adapters.Length > 0 ? Adapters[0] : null;
        AdapterNeedsBaudrate = CanAdapters.NeedsBaudrate(type);
    }

    public SuiteSettingsViewModel(AppSettings s)
    {
        _showGraphs = s.ShowGraphs;
        _autoLoadLastFile = s.AutoLoadLastFile;
        // values past Easy (the bar views) show as Easy, like the old combo
        _defaultViewType = System.Math.Min((int)s.DefaultViewType, 2);
        _colorBlindMode = (int)MapControls.HeatColor.ParseMode(s.ColorBlindMode);
        _symbolListFontSize = Math.Clamp(s.SymbolListFontSize, MinFontSize, MaxFontSize);
        _mapCellGap = Math.Clamp(s.MapCellGap, 0, MaxCellGap);
        _synchronizeMapviewers = s.SynchronizeMapviewers;
        _autoChecksum = s.AutoChecksum;
        _showAddressesInHex = s.ShowAddressesInHex;
        _requestProjectNotes = s.RequestProjectNotes;
        _hideSymbolTable = s.HideSymbolTable;
        _showMapPreviewPopup = s.ShowMapPreviewPopup;
        _showSymbolDescriptionLine = s.ShowSymbolDescriptionLine;
        _showRibbon = s.ShowRibbon;
        _projectFolder = s.ProjectFolder;
        _adapter = s.Adapter;
        _baudrate = s.Baudrate;
        _onlyPBus = s.OnlyPBus;
        _autoUpdateSRAMViewers = s.AutoUpdateSRAMViewers;
        _interpolateLogWorksTimescale = s.InterpolateLogWorksTimescale;
        _autoUpdateInterval = Math.Clamp(s.AutoUpdateInterval, 5, 60);
        _useDigitalWidebandLambda = s.UseDigitalWidebandLambda;
        _widebandDevice = Array.Exists(WidebandDevices, d => d == s.WidebandDevice) ? s.WidebandDevice : "LC1";
        _wbPort = s.WbPort;
        _measureAFRInLambda = s.MeasureAFRInLambda;
        _useWidebandLambda = s.UseWidebandLambda;
        if (s.UseWidebandLambda) _useDigitalWidebandLambda = false;
        _wideBandSymbol = Array.Exists(WidebandSymbols, w => w == s.WideBandSymbol) ? s.WideBandSymbol : DefaultWidebandSymbol;
        _widebandLowVoltage = (decimal)s.WidebandLowVoltage / 1000;
        _widebandHighVoltage = (decimal)s.WidebandHighVoltage / 1000;
        _widebandLowAFR = (decimal)s.WidebandLowAFR / 1000;
        _widebandHighAFR = (decimal)s.WidebandHighAFR / 1000;
        PropertyChanged += (_, e) => { if (e.PropertyName == nameof(UseDigitalWidebandLambda) && UseDigitalWidebandLambda) UseWidebandLambda = false; };
        // not the field: the setter fills the adapter list
        AdapterType = Array.Exists(AdapterTypes, a => a == s.AdapterType) ? s.AdapterType : AdapterTypes[0];
        m_opened = true;
    }

    /// <summary>OK: every value back into AppSettings (each setter saves).</summary>
    public virtual void Apply(AppSettings s)
    {
        s.ShowGraphs = ShowGraphs;
        s.AutoLoadLastFile = AutoLoadLastFile;
        s.DefaultViewType = (SuiteViewType)DefaultViewType;
        s.ColorBlindMode = ColorBlindModes[ColorBlindMode];
        s.SymbolListFontSize = (int)(SymbolListFontSize ?? 13);
        s.MapCellGap = (int)(MapCellGap ?? 0);
        s.SynchronizeMapviewers = SynchronizeMapviewers;
        s.AutoChecksum = AutoChecksum;
        s.ShowAddressesInHex = ShowAddressesInHex;
        s.RequestProjectNotes = RequestProjectNotes;
        s.HideSymbolTable = HideSymbolTable;
        s.ShowMapPreviewPopup = ShowMapPreviewPopup;
        s.ShowSymbolDescriptionLine = ShowSymbolDescriptionLine;
        s.ShowRibbon = ShowRibbon;
        s.AdapterType = AdapterType;
        s.Adapter = Adapter ?? "";
        s.Baudrate = Baudrate;
        s.OnlyPBus = OnlyPBus;
        s.AutoUpdateSRAMViewers = AutoUpdateSRAMViewers;
        s.InterpolateLogWorksTimescale = InterpolateLogWorksTimescale;
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
