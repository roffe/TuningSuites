using System.Collections.Generic;
using System.IO;
using CommonSuite;
using CommunityToolkit.Mvvm.ComponentModel;
using T7;
using SuiteApp.ViewModels;

namespace T7App.ViewModels;

/// <summary>frmSearchMaps ("Search maps for value...").</summary>
public partial class SearchMapsViewModel : ObservableObject
{
    [ObservableProperty] private bool _searchForNumericValues = true;
    [ObservableProperty] private decimal? _numericValue = 0;
    [ObservableProperty] private bool _searchForStringValues;
    [ObservableProperty] private string _stringValue = "";
    [ObservableProperty] private bool _includeSymbolNames;
    [ObservableProperty] private bool _includeSymbolDescription;
    [ObservableProperty] private bool _useSpecificMapLength;
    [ObservableProperty] private decimal? _mapLength = 0;

    public MapSearchOptions ToOptions() => new(SearchForNumericValues, NumericValue ?? 0, SearchForStringValues, StringValue ?? "",
        IncludeSymbolNames, IncludeSymbolDescription, UseSpecificMapLength, (int)(MapLength ?? 0));
}

/// <summary>"Search results: file": the symbols found, opened like from the symbol list.</summary>
public class SearchResultsViewModel(T7MainWindowViewModel owner, string file, List<SymbolHelper> results) : DocumentViewModel
{
    public override string Title => $"Search results: {Path.GetFileName(file)}";

    public List<SymbolHelper> Results { get; } = results;

    public void Open(SymbolHelper? sh)
    {
        if (sh != null) owner.OpenSymbolByName(sh.SmartVarname);
    }
}
