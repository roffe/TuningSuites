using CommonSuite;
using SuiteApp.ViewModels;
using T5App.ViewModels;
using T7App.ViewModels;
using T8App.ViewModels;

namespace TxSuite;

/// <summary>
/// TxSuite's settings: what every suite shares once (Common), then each ECU's own (T5, T7, T8). Ok writes all three suites' settings:
/// the suite in front edits its live settings, the others their files, which they read when they come to the front.
/// </summary>
public sealed class TxSettingsViewModel
{
    private readonly AppSettings m_t5, m_t7, m_t8;
    private readonly Trionic5Tools.T5AppSettings m_t5Own;

    public SuiteSettingsViewModel Common { get; }
    public T5App.ViewModels.SettingsViewModel T5 { get; }
    public T7App.ViewModels.SettingsViewModel T7 { get; }
    public T8App.ViewModels.SettingsViewModel T8 { get; }

    // the autotune dialogs' Ok, saved with the settings' Ok (as in T5Suite and T7Suite)
    public T5App.ViewModels.AutotuneSettingsViewModel? T5Autotune { get; set; }
    public T7App.ViewModels.AutotuneSettingsViewModel? T7Autotune { get; set; }

    public TxSettingsViewModel(MainWindowViewModel current)
    {
        var t5 = current as T5MainWindowViewModel;
        m_t5 = t5?.Settings ?? new AppSettings(new Trionic5Tools.T5SuiteRegistry());
        m_t5Own = t5?.T5Settings ?? new Trionic5Tools.T5AppSettings();
        m_t7 = current is T7MainWindowViewModel ? current.Settings : new AppSettings(new T7.T7SuiteRegistry());
        m_t8 = current is T8MainWindowViewModel ? current.Settings : new AppSettings(new T8SuitePro.T8SuiteRegistry());
        // the shared options as the suite in front has them; Ok gives them to the other two
        Common = new SuiteSettingsViewModel(current.Settings);
        T5 = new(m_t5, m_t5Own);
        T7 = new(m_t7);
        T8 = new(m_t8);
    }

    public T5App.ViewModels.AutotuneSettingsViewModel NewT5Autotune() => T5Autotune?.Clone() ?? new(m_t5, m_t5Own);

    public T7App.ViewModels.AutotuneSettingsViewModel NewT7Autotune() => T7Autotune?.Clone() ?? new(m_t7);

    public void Apply()
    {
        foreach (SuiteSettingsViewModel ecu in new SuiteSettingsViewModel[] { T5, T7, T8 }) CopyCommon(Common, ecu);
        // each suite's order (T5App's and T7App's OnSettings)
        T5Autotune?.Apply(m_t5, m_t5Own);
        T5.Apply(m_t5, m_t5Own);
        T7.Apply(m_t7);
        T7Autotune?.Apply(m_t7);
        T8.Apply(m_t8);
    }

    // the Common tab's options; the ones an ECU tab shows (P-bus, the wideband through a symbol) stay the ECU's. The wideband on a com
    // port and through a symbol exclude each other: a com port wideband turns the symbol one off in every suite
    private static void CopyCommon(SuiteSettingsViewModel from, SuiteSettingsViewModel to)
    {
        to.ShowRedWhite = from.ShowRedWhite;
        to.ShowGraphs = from.ShowGraphs;
        to.DisableMapviewerColors = from.DisableMapviewerColors;
        to.AutoLoadLastFile = from.AutoLoadLastFile;
        to.DefaultViewType = from.DefaultViewType;
        to.ColorBlindMode = from.ColorBlindMode;
        to.SymbolListFontSize = from.SymbolListFontSize;
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
