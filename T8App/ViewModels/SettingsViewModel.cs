using CommonSuite;
using CommunityToolkit.Mvvm.ComponentModel;
using SuiteApp.ViewModels;

namespace T8App.ViewModels;

/// <summary>frmSettings, T8Suite's: the shared settings plus Auto mapdetection active. The realtime group comes with the ECU.</summary>
public partial class SettingsViewModel : SuiteSettingsViewModel
{
    [ObservableProperty] private bool _mapDetectionActive;

    public SettingsViewModel(AppSettings s) : base(s) => _mapDetectionActive = s.MapDetectionActive;

    public override void Apply(AppSettings s)
    {
        base.Apply(s);
        s.MapDetectionActive = MapDetectionActive;
    }
}
