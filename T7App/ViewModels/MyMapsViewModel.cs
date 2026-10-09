using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using T7;

namespace T7App.ViewModels;

public partial class MyMapRow : ObservableObject
{
    [ObservableProperty] private string _category = "";
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _symbol = "";
}

/// <summary>frmDefineMyMaps: category / title / symbol rows.</summary>
public class MyMapsViewModel(IEnumerable<MapShortcut> maps)
{
    public ObservableCollection<MyMapRow> Rows { get; } = new(maps.Select(m => new MyMapRow { Category = m.Group, Title = m.Caption, Symbol = m.Symbol }));

    public MyMapRow Add()
    {
        var row = new MyMapRow { Category = Rows.LastOrDefault()?.Category ?? "My maps" };
        Rows.Add(row);
        return row;
    }

    public List<MapShortcut> Maps => Rows.Where(r => r.Symbol.Trim() != "")
        .Select(r => new MapShortcut(r.Category.Trim(), r.Title.Trim() == "" ? r.Symbol.Trim() : r.Title.Trim(), r.Symbol.Trim())).ToList();
}
