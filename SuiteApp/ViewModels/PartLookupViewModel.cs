using System;
using CommonSuite;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SuiteApp.ViewModels;

/// <summary>frmPartnumberLookup: the suite's lookup (TxSuite's: every ECU's); T8Suite only showed the car model and engine type.</summary>
public partial class PartLookupViewModel(Func<string, PartInfo?> lookup, string suite) : ObservableObject
{
    [ObservableProperty] private string _partNumber = "";
    [ObservableProperty] private PartInfo? _info;
    [ObservableProperty] private string _message = "";

    /// <summary>Power, torque and the engine boxes (PartInfo.Details).</summary>
    public bool Details => Info?.Details ?? true;

    public bool HasBinary => Info?.Binary != null;

    /// <summary>TxSuite: the open suite's own lookup. Compare takes only its own ECU's binaries (no partnumber is in two suites' lists).</summary>
    public Func<string, PartInfo?>? Own { get; init; }

    public bool CanCompare => HasBinary && (Own == null || Own(Info!.PartNumber) != null);

    /// <summary>T5Suite's partnumber list (the box button); null hides the button.</summary>
    public Func<System.Threading.Tasks.Task<string?>>? Browse { get; init; }

    public bool CanBrowse => Browse != null;

    public async System.Threading.Tasks.Task BrowseAsync()
    {
        if (Browse != null && await Browse() is { } partNumber)
        {
            PartNumber = partNumber;
            Lookup();
        }
    }

    partial void OnInfoChanged(PartInfo? value)
    {
        OnPropertyChanged(nameof(HasBinary));
        OnPropertyChanged(nameof(CanCompare));
        OnPropertyChanged(nameof(Details));
    }

    // T8Suite said T7Suite too
    public void Lookup()
    {
        Info = lookup(PartNumber.Trim());
        Message = Info == null ? $"The entered partnumber was not recognized by {suite}" : "";
    }
}
