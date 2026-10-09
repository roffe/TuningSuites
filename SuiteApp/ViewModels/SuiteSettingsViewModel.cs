using CommonSuite;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SuiteApp.ViewModels;

/// <summary>
/// frmSettings, the settings both suites have that do something here (of the docking / window size options only Hide symbol
/// window); each suite's dialog adds its own.
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
        // an empty folder fell back to <program>\Projects; the program folder isn't writable on Linux, so the default instead
        s.ProjectFolder = string.IsNullOrWhiteSpace(ProjectFolder)
            ? System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), "TxSuite", "Projects")
            : ProjectFolder;
    }
}
