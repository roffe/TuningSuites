using CommonSuite;
using CommunityToolkit.Mvvm.ComponentModel;
using SuiteApp.ViewModels;

namespace T7App.ViewModels;

/// <summary>
/// frmSettings: the shared settings and connection plus T7Suite's own (closed loop indicator, timestamp marker, footer fix, T7Suite AFR
/// maps; its CAN log is Settings → Logging's CAN frames); the autotune options are behind its "Autotune settings" button
/// (AutotuneSettingsViewModel).
/// </summary>
public partial class SettingsViewModel : SuiteSettingsViewModel
{
    [ObservableProperty] private int _standardFill;
    [ObservableProperty] private bool _writeTimestampInBinary;
    [ObservableProperty] private bool _autoFixFooter;

    // AFR maps (the wideband options are shared)
    [ObservableProperty] private bool _autoCreateAFRMaps;

    public string[] ClosedLoopIndicators { get; } = ["No closed loop indicator", "Square closed loop indicator", "Triangle closed loop indicator"];

    public SettingsViewModel(AppSettings s) : base(s)
    {
        _standardFill = System.Math.Clamp(s.StandardFill, 0, 2);
        _writeTimestampInBinary = s.WriteTimestampInBinary;
        _autoFixFooter = s.AutoFixFooter;
        _autoCreateAFRMaps = s.AutoCreateAFRMaps;
    }

    public override void Apply(AppSettings s)
    {
        base.Apply(s);
        s.StandardFill = StandardFill;
        s.WriteTimestampInBinary = WriteTimestampInBinary;
        s.AutoFixFooter = AutoFixFooter;
        s.AutoCreateAFRMaps = AutoCreateAFRMaps;
    }
}
