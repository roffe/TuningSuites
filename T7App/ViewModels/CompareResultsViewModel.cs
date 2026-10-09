using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Collections;
using CommonSuite;
using CommunityToolkit.Mvvm.ComponentModel;
using T7;

namespace T7App.ViewModels;

/// <summary>CompareResults: the symbols that differ between the open bin and another, grouped by category.</summary>
public partial class CompareResultsViewModel : DocumentViewModel
{
    private readonly MainWindowViewModel m_owner;

    public T7Binary Current { get; }
    public T7Binary Other { get; }
    public DataGridCollectionView Rows { get; }

    [ObservableProperty]
    private CompareRow? _selected;

    public override string Title => $"Compare results: {Path.GetFileName(Other.FileName)}";

    public CompareResultsViewModel(MainWindowViewModel owner, T7Binary current, T7Binary other, List<CompareRow> rows)
    {
        m_owner = owner;
        Current = current;
        Other = other;
        Rows = new DataGridCollectionView(rows);
        Rows.GroupDescriptions.Add(new DataGridPathGroupDescription(nameof(CompareRow.Category)));
    }

    /// <summary>
    /// Double-click / Enter (tabdet_onSymbolSelect): the map of the open file, and the other file's map in a read-only
    /// compare viewer.
    /// </summary>
    public void Open(CompareRow? row)
    {
        if (row == null) return;
        if (Current.FindAny(row.SymbolName) is { } mine) m_owner.OpenSymbolByName(mine.SmartVarname);
        if (Other.FindAny(row.SymbolName) is { } theirs && MapViewerViewModel.Create(m_owner, Other, theirs, readOnly: true) is { } viewer)
            m_owner.ShowDocument(viewer);
    }

    /// <summary>"Show differences map": |other − open file| per value, read-only, axes from the open file.</summary>
    public void ShowDifferenceMap(CompareRow? row)
    {
        if (row == null || Current.FindAny(row.SymbolName) is not { } mine) return;
        byte[] theirs = Other.Read((int)row.FlashAddress, row.LengthBytes);
        byte[]? ours = Current.ReadSymbol(mine);
        byte[]? diff = ours == null ? null : T7Compare.DifferenceMap(theirs, ours, Current.IsSixteenBitTable(mine.SmartVarname));
        if (diff == null)
        {
            m_owner.ShowInfo("Map lengths don't match...");
            return;
        }
        string title = $"Symbol difference: {mine.SmartVarname} [{Path.GetFileName(Other.FileName)}]";
        if (MapViewerViewModel.Create(m_owner, Current, mine, diff, readOnly: true, title: title) is { } viewer) m_owner.ShowDocument(viewer);
    }

    /// <summary>"Export to Excel" becomes CSV: the grid's columns, one row per result.</summary>
    public void ExportCsv(string file)
    {
        var sb = new StringBuilder("Symbol;Description;Length (bytes);Percentage of values different;Number of values different;Average difference;Symbolnumber #1;Symbolnumber #2;User description;Missing in original file;Missing in compare file;Category\n");
        foreach (CompareRow r in Rows.SourceCollection.Cast<CompareRow>())
        {
            sb.AppendJoin(';', r.SymbolName, r.Description.Replace(';', ','), r.LengthBytes, r.Percentage.ToString("F1", CultureInfo.InvariantCulture),
                r.Differences, r.AverageDifference.ToString("F1", CultureInfo.InvariantCulture), r.SymbolNumber1, r.SymbolNumber2, r.Userdescription,
                r.MissingInOriFile, r.MissingInCompareFile, r.Category).Append('\n');
        }
        File.WriteAllText(file, sb.ToString());
    }
}

public partial class TransferItem(string name, bool selected) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty]
    private bool _selected = selected;
}

/// <summary>frmTransferSelectionSymbolsSelection: the offered symbols by name, the last selection checked.</summary>
public class TransferSelectionViewModel(IEnumerable<SymbolHelper> candidates, ISet<string> lastSelection)
{
    public List<TransferItem> Items { get; } = candidates.Select(s => new TransferItem(s.Varname, lastSelection.Contains(s.Varname))).ToList();

    public void SetAll(bool selected)
    {
        foreach (TransferItem i in Items) i.Selected = selected;
    }

    public HashSet<string> Selection => Items.Where(i => i.Selected).Select(i => i.Name).ToHashSet();
}
