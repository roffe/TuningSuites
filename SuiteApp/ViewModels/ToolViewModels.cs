using System.Collections.Generic;
using System.IO;
using CommonSuite;

namespace SuiteApp.ViewModels;

/// <summary>Browse axis information: every map with its axes; double-clicking opens the map or the clicked axis.</summary>
public class AxisBrowserViewModel(MainWindowViewModel owner, SuiteBinary bin, string? only = null) : DocumentViewModel
{
    public override string Title => "Axis browser: " + Path.GetFileName(bin.FileName) + (only == null ? "" : " " + only);

    public List<AxisInfo> Rows { get; } = bin.AxisRows(only);

    public void Open(string? symbol)
    {
        if (!string.IsNullOrEmpty(symbol) && bin.FindAny(symbol) != null) owner.OpenSymbolByName(symbol);
    }
}
