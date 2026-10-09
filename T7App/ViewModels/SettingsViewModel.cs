using CommonSuite;
using CommunityToolkit.Mvvm.ComponentModel;

namespace T7App.ViewModels;

/// <summary>
/// frmSettings, the offline part: the settings that do something in this app. The docking / window size options don't
/// apply to tabs; the realtime group comes with the ECU features.
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
    [ObservableProperty] private string _projectFolder;

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
        _projectFolder = s.ProjectFolder;
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
        // an empty folder fell back to <program>\Projects; the program folder isn't writable on Linux, so the default instead
        s.ProjectFolder = string.IsNullOrWhiteSpace(ProjectFolder)
            ? System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), "TxSuite", "Projects")
            : ProjectFolder;
    }
}
