using System.Collections.Generic;
using CommonSuite;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SuiteApp.ViewModels;

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

/// <summary>"Search results: ...": the symbols found, opened like from the symbol list.</summary>
public class SearchResultsViewModel(MainWindowViewModel owner, string title, List<SymbolHelper> results) : DocumentViewModel
{
    public override string Title => title;

    public List<SymbolHelper> Results { get; } = results;

    public void Open(SymbolHelper? sh)
    {
        if (sh != null) owner.OpenSymbolByName(sh.SmartVarname);
    }
}
