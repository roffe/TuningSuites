using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommonSuite;
using CommunityToolkit.Mvvm.ComponentModel;
using T7;
using SuiteApp.ViewModels;

namespace T7App.ViewModels;

/// <summary>A row of the package editor: the map and its bytes as the .t7p writes them ("XX,XX,...").</summary>
public partial class TuningPackageRow : ObservableObject
{
    [ObservableProperty] private string _map = "";
    [ObservableProperty] private int _length;
    [ObservableProperty] private string _data = "";

    public byte[]? Bytes
    {
        get
        {
            try
            {
                return Data.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(b => Convert.ToByte(b.Trim(), 16)).ToArray();
            }
            catch (FormatException)
            {
                return null;
            }
        }
        set
        {
            Data = value == null ? "" : string.Concat(value.Select(b => b.ToString("X2") + ","));
            Length = value?.Length ?? 0;
        }
    }
}

/// <summary>
/// frmEditTuningPackage ("Edit a tuning package..."): a package's maps; symbols added from the symbol list, rows removed, a row's
/// map opened next to the bin's in a viewer whose save goes back into the row, and the result saved as a new .t7p.
/// </summary>
public class TuningPackageEditorViewModel(T7MainWindowViewModel owner, T7Binary bin) : DocumentViewModel
{
    public override string Title => "Edit a tuning package" + (PackageFile == null ? "" : ": " + Path.GetFileName(PackageFile));

    public ObservableCollection<TuningPackageRow> Rows { get; } = [];

    public string? PackageFile { get; private set; }

    /// <summary>Loads a .t7p's symbol entries; a broken data line leaves the row empty.</summary>
    public void Open(string file)
    {
        PackageFile = file;
        OnPropertyChanged(nameof(Title));
        Rows.Clear();
        var row = new TuningPackageRow();
        foreach (string raw in File.ReadLines(file))
        {
            string line = raw.Trim();
            if (line.StartsWith("symbol=")) row = new TuningPackageRow { Map = line[7..] };
            else if (line.StartsWith("length=") && int.TryParse(line[7..], out int length)) row.Length = length;
            else if (line.StartsWith("data="))
            {
                row.Data = line[5..];
                if (row.Bytes == null) row.Data = "";
                Rows.Add(row);
            }
        }
    }

    /// <summary>Dragging symbols in: a new row, or the existing row's data, from the bin.</summary>
    public void Add(IEnumerable<SymbolHelper> symbols)
    {
        foreach (SymbolHelper sh in symbols)
        {
            if (bin.ReadSymbol(sh) is not { } data) continue;
            TuningPackageRow? row = Rows.FirstOrDefault(r => r.Map == sh.SmartVarname);
            if (row == null) Rows.Add(row = new TuningPackageRow { Map = sh.SmartVarname });
            row.Bytes = data;
        }
    }

    public void Remove(IEnumerable<TuningPackageRow> rows)
    {
        foreach (TuningPackageRow r in rows.ToList()) Rows.Remove(r);
    }

    /// <summary>Double-click: the bin's map, and the package's in a "Tuning package symbol" viewer that saves into the row.</summary>
    public void OpenRow(TuningPackageRow row)
    {
        if (bin.FindAny(row.Map) is not { } sh)
        {
            owner.ShowInfo($"Symbol {row.Map} does not exist in this file");
            return;
        }
        owner.OpenSymbolByName(sh.SmartVarname);
        if (row.Bytes is not { Length: > 0 } data) return;
        string title = $"Tuning package symbol: {row.Map} [{Path.GetFileName(PackageFile ?? "new package")}]";
        if (MapViewerViewModel.Create(owner, bin, sh, data, title: title, sram: true) is not { } viewer) return;
        viewer.OnlineMode = true;
        viewer.NoEcu = true;
        viewer.SaveTo = bytes => row.Bytes = bytes;
        viewer.ReadFrom = () => row.Bytes ?? data;
        owner.ShowDocument(viewer);
    }

    /// <summary>
    /// Ok: every row as symbol / length / data, its own bytes (T7Suite took the lengths from the bin and dropped rows the bin
    /// lacks, which could write a length that didn't match the data).
    /// </summary>
    public void Save(string file)
    {
        if (File.Exists(file)) File.Delete(file);
        var exporter = new PackageExporter();
        foreach (TuningPackageRow r in Rows)
            if (r.Bytes is { Length: > 0 } data) exporter.ExportMap(file, r.Map, "", data.Length, data);
        PackageFile = file;
        OnPropertyChanged(nameof(Title));
    }
}
