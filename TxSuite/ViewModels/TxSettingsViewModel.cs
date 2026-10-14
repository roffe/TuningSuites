using CommonSuite;
using SuiteApp.ViewModels;

namespace TxSuite.ViewModels;

/// <summary>
/// TxSuite's settings: what every ECU shares (Common), the log files (Logging), then each ECU's own (T5, T7, T8), as the old suites'
/// frmSettings had them. Ok writes each ECU's live settings: the shared options go once to the top of TxSuite's settings, each tab's into
/// its ECU's section.
/// </summary>
public sealed class TxSettingsViewModel
{
    private readonly AppSettings m_t5, m_t7, m_t8;
    private readonly Trionic5Tools.T5AppSettings m_t5Own;

    public SuiteSettingsViewModel Common { get; }

    /// <summary>The log files: on or off, their levels, how long they're kept (the program's, not an ECU's).</summary>
    public LoggingSettingsViewModel Logging { get; } = new();

    public T5App.ViewModels.SettingsViewModel T5 { get; }
    public T7App.ViewModels.SettingsViewModel T7 { get; }
    public T8App.ViewModels.SettingsViewModel T8 { get; }

    // the autotune dialogs' Ok, saved with the settings' Ok (as in T5Suite and T7Suite)
    public T5App.ViewModels.AutotuneSettingsViewModel? T5Autotune { get; set; }
    public T7App.ViewModels.AutotuneSettingsViewModel? T7Autotune { get; set; }

    public TxSettingsViewModel(MainViewModel main)
    {
        m_t5 = main.T5.Settings;
        m_t5Own = main.T5.T5Settings;
        m_t7 = main.T7.Settings;
        m_t8 = main.T8.Settings;
        // the shared options as the open file's ECU has them; Ok gives them to every ECU
        Common = new SuiteSettingsViewModel(main.Settings);
        T5 = new(m_t5, m_t5Own);
        T7 = new(m_t7);
        T8 = new(m_t8);
    }

    public T5App.ViewModels.AutotuneSettingsViewModel NewT5Autotune() => T5Autotune?.Clone() ?? new(m_t5, m_t5Own);

    public T7App.ViewModels.AutotuneSettingsViewModel NewT7Autotune() => T7Autotune?.Clone() ?? new(m_t7);

    public void Apply()
    {
        Logging.Apply();
        foreach (SuiteSettingsViewModel ecu in new SuiteSettingsViewModel[] { T5, T7, T8 }) CopyCommon(Common, ecu);
        // each old suite's order (T5Suite's and T7Suite's frmSettings)
        T5Autotune?.Apply(m_t5, m_t5Own);
        T5.Apply(m_t5, m_t5Own);
        T7.Apply(m_t7);
        T7Autotune?.Apply(m_t7);
        T8.Apply(m_t8);
    }

    // the Common tab's options (AppSettings.SharedNames); the ones an ECU tab shows (P-bus, the wideband through a symbol) stay the ECU's.
    // The wideband on a com port and through a symbol exclude each other: a com port wideband turns the symbol one off for every ECU
    private static void CopyCommon(SuiteSettingsViewModel from, SuiteSettingsViewModel to)
    {
        to.ShowRedWhite = from.ShowRedWhite;
        to.ShowGraphs = from.ShowGraphs;
        to.DisableMapviewerColors = from.DisableMapviewerColors;
        to.AutoLoadLastFile = from.AutoLoadLastFile;
        to.DefaultViewType = from.DefaultViewType;
        to.ColorBlindMode = from.ColorBlindMode;
        to.SymbolListFontSize = from.SymbolListFontSize;
        to.MapCellGap = from.MapCellGap;
        to.SynchronizeMapviewers = from.SynchronizeMapviewers;
        to.AutoChecksum = from.AutoChecksum;
        to.ShowAddressesInHex = from.ShowAddressesInHex;
        to.RequestProjectNotes = from.RequestProjectNotes;
        to.HideSymbolTable = from.HideSymbolTable;
        to.ShowMapPreviewPopup = from.ShowMapPreviewPopup;
        to.ProjectFolder = from.ProjectFolder;
        // the type first: a new type refills the adapter list
        to.AdapterType = from.AdapterType;
        to.Adapter = from.Adapter;
        to.Baudrate = from.Baudrate;
        to.AutoUpdateSRAMViewers = from.AutoUpdateSRAMViewers;
        to.AutoUpdateInterval = from.AutoUpdateInterval;
        to.InterpolateLogWorksTimescale = from.InterpolateLogWorksTimescale;
        to.MeasureAFRInLambda = from.MeasureAFRInLambda;
        to.UseDigitalWidebandLambda = from.UseDigitalWidebandLambda;
        to.WidebandDevice = from.WidebandDevice;
        to.WbPort = from.WbPort;
    }
}
