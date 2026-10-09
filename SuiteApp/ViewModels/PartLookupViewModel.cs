using System;
using CommonSuite;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SuiteApp.ViewModels;

/// <summary>frmPartnumberLookup: the suite's lookup; T8Suite only showed the car model and engine type.</summary>
public partial class PartLookupViewModel(Func<string, PartInfo?> lookup, bool details) : ObservableObject
{
    [ObservableProperty] private string _partNumber = "";
    [ObservableProperty] private PartInfo? _info;
    [ObservableProperty] private string _message = "";

    /// <summary>Power, torque and the engine boxes (T7Suite).</summary>
    public bool Details { get; } = details;

    public bool HasBinary => Info?.Binary != null;

    partial void OnInfoChanged(PartInfo? value) => OnPropertyChanged(nameof(HasBinary));

    // both suites said T7Suite
    public void Lookup()
    {
        Info = lookup(PartNumber.Trim());
        Message = Info == null ? "The entered partnumber was not recognized by T7Suite" : "";
    }
}
