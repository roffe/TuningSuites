using CommonSuite;
using CommunityToolkit.Mvvm.ComponentModel;
using SuiteApp.ViewModels;

namespace T8App.ViewModels;

/// <summary>
/// frmSettings, T8Suite's: the shared settings, connection and serial wideband plus Auto mapdetection active, Use Legion
/// Bootloader and Prefer dynamic retrieval of live data.
/// </summary>
public partial class SettingsViewModel : SuiteSettingsViewModel
{
    [ObservableProperty] private bool _mapDetectionActive;
    [ObservableProperty] private bool _useLegionBootloader;
    [ObservableProperty] private bool _preferDynamicLiveData;

    public SettingsViewModel(AppSettings s) : base(s)
    {
        _mapDetectionActive = s.MapDetectionActive;
        _useLegionBootloader = s.UseLegionBootloader;
        _preferDynamicLiveData = s.PreferDynamicLiveData;
    }

    public override void Apply(AppSettings s)
    {
        base.Apply(s);
        s.MapDetectionActive = MapDetectionActive;
        s.UseLegionBootloader = UseLegionBootloader;
        s.PreferDynamicLiveData = PreferDynamicLiveData;
    }
}
