using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using T7;
using SuiteApp.ViewModels;

namespace T7App.ViewModels;

/// <summary>Browse axis information: every map with its axes; double-clicking opens the map or the clicked axis.</summary>
public class AxisBrowserViewModel(T7MainWindowViewModel owner, T7Binary bin, string? only = null) : DocumentViewModel
{
    public override string Title => "Axis browser: " + Path.GetFileName(bin.FileName) + (only == null ? "" : " " + only);

    public List<AxisInfo> Rows { get; } = BinaryTools.Axes(bin, only);

    public void Open(string? symbol)
    {
        if (!string.IsNullOrEmpty(symbol) && bin.FindAny(symbol) != null) owner.OpenSymbolByName(symbol);
    }
}

/// <summary>frmPartnumberLookup.</summary>
public partial class PartLookupViewModel : ObservableObject
{
    [ObservableProperty] private string _partNumber = "";
    [ObservableProperty] private PartInfo? _info;
    [ObservableProperty] private string _message = "";

    public bool HasBinary => Info?.Binary != null;

    partial void OnInfoChanged(PartInfo? value) => OnPropertyChanged(nameof(HasBinary));

    public void Lookup()
    {
        Info = BinaryTools.LookupPartNumber(PartNumber);
        Message = Info == null ? "The entered partnumber was not recognized by T7Suite" : "";
    }
}
