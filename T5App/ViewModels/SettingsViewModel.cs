using CommonSuite;
using SuiteApp.ViewModels;
using Trionic5Tools;

namespace T5App.ViewModels;

/// <summary>frmSettings, T5Suite's: the shared settings, connection and serial wideband; T5's own groups come with their features.</summary>
public partial class SettingsViewModel : SuiteSettingsViewModel
{
    public SettingsViewModel(AppSettings s, T5AppSettings t5) : base(s)
    {
        EnableAdvancedMode = t5.EnableAdvancedMode;
        AutoDetectMapsensorType = t5.AutoDetectMapsensorType;
    }

    /// <summary>"Auto detect mapsensor type": a file whose marker says stock gets its sensor from the maps.</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool _autoDetectMapsensorType;

    /// <summary>"Use wideband lambda through symbol": T5Suite's AD_EGR (pin 69); AD_cat (pin 70) converts the same way here.</summary>
    public override string[] WidebandSymbols => ["AD_EGR", "AD_cat"];

    /// <summary>"Advanced mode enabled": shows the advanced tuning wizards.</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool _enableAdvancedMode;

    public void Apply(AppSettings s, T5AppSettings t5)
    {
        base.Apply(s);
        t5.EnableAdvancedMode = EnableAdvancedMode;
        t5.AutoDetectMapsensorType = AutoDetectMapsensorType;
    }
}
