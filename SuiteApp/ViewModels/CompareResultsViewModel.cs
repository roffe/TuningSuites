using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Collections;
using CommonSuite;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SuiteApp.ViewModels;

/// <summary>One side of a compare: a file's bytes for a symbol name, and how its viewer opens.</summary>
public record CompareSide(string FileName, Func<string, byte[]?> Read, Action<string> Open)
{
    /// <summary>The open bin: its own viewer, the one that edits.</summary>
    public static CompareSide Bin(MainWindowViewModel owner, SuiteBinary bin) =>
        new(bin.FileName, name => bin.FindAny(name) is { } sh ? bin.ReadSymbol(sh) : null,
            name => { if (bin.FindAny(name) is { } sh) owner.OpenSymbolByName(sh.SmartVarname); });

    /// <summary>Another bin: a read-only compare viewer.</summary>
    public static CompareSide OtherBin(MainWindowViewModel owner, SuiteBinary bin) =>
        new(bin.FileName, name => bin.FindAny(name) is { } sh ? bin.ReadSymbol(sh) : null,
            name =>
            {
                if (bin.FindAny(name) is { } sh && MapViewerViewModel.Create(owner, bin, sh, readOnly: true) is { } viewer) owner.ShowDocument(viewer);
            });
}

/// <summary>
/// CompareResults / SRAMCompareResults: the symbols that differ between two sides (bin and bin, bin and SRAM snapshot, two
/// snapshots), grouped by category. Viewers use the open bin's symbols and axes.
/// </summary>
public partial class CompareResultsViewModel : DocumentViewModel
{
    private readonly MainWindowViewModel m_owner;
    private readonly string m_title, m_differenceTitle;

    public SuiteBinary Current { get; }
    public CompareSide First { get; }
    public CompareSide Second { get; }
    public DataGridCollectionView Rows { get; }

    [ObservableProperty]
    private CompareRow? _selected;

    public override string Title => m_title;

    public CompareResultsViewModel(MainWindowViewModel owner, SuiteBinary current, CompareSide first, CompareSide second, List<CompareRow> rows,
        string title, string differenceTitle)
    {
        m_owner = owner;
        Current = current;
        First = first;
        Second = second;
        m_title = title;
        m_differenceTitle = differenceTitle;
        Rows = new DataGridCollectionView(rows);
        Rows.GroupDescriptions.Add(new DataGridPathGroupDescription(nameof(CompareRow.Category)));
    }

    /// <summary>Compare with another bin: "Compare results: other.bin".</summary>
    public static CompareResultsViewModel Binaries(MainWindowViewModel owner, SuiteBinary current, SuiteBinary other, List<CompareRow> rows) =>
        new(owner, current, CompareSide.Bin(owner, current), CompareSide.OtherBin(owner, other), rows,
            $"Compare results: {Path.GetFileName(other.FileName)}", $"Symbol difference: {{0}} [{Path.GetFileName(other.FileName)}]");

    /// <summary>Double-click / Enter (tabdet_onSymbolSelect): both sides' viewers.</summary>
    public void Open(CompareRow? row)
    {
        if (row == null) return;
        First.Open(row.SymbolName);
        Second.Open(row.SymbolName);
    }

    /// <summary>"Show differences map": |second − first| per value, read-only, axes from the open file.</summary>
    public void ShowDifferenceMap(CompareRow? row)
    {
        if (row == null || Current.FindAny(row.SymbolName) is not { } mine) return;
        byte[]? a = First.Read(row.SymbolName), b = Second.Read(row.SymbolName);
        byte[]? diff = a == null || b == null ? null : SuiteCompare.DifferenceMap(b, a, Current.IsSixteenBitTable(mine.SmartVarname));
        if (diff == null)
        {
            m_owner.ShowInfo("Map lengths don't match...");
            return;
        }
        string title = string.Format(m_differenceTitle, mine.SmartVarname);
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
public class TransferSelectionViewModel(IEnumerable<string> candidates, ISet<string> lastSelection)
{
    public List<TransferItem> Items { get; } = candidates.Select(n => new TransferItem(n, lastSelection.Contains(n))).ToList();

    public void SetAll(bool selected)
    {
        foreach (TransferItem i in Items) i.Selected = selected;
    }

    public HashSet<string> Selection => Items.Where(i => i.Selected).Select(i => i.Name).ToHashSet();
}
