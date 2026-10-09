using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Collections;
using CommonSuite;
using CommunityToolkit.Mvvm.ComponentModel;
using T7;

namespace T7App.ViewModels;

/// <summary>A row of "SID information selection".</summary>
public partial class SidRow : ObservableObject
{
    private readonly T7Binary m_bin;

    public SIDIHelper Entry { get; }

    public SidRow(T7Binary bin, SIDIHelper entry)
    {
        m_bin = bin;
        Entry = entry;
    }

    public bool IsReadOnly => Entry.IsReadOnly;
    public bool CanEdit => !Entry.IsReadOnly;
    public string ModeDescr => Entry.ModeDescr;
    public string Info => Entry.Info;
    public string AddressText => Entry.AddressSRAM.ToString("X6");
    public string FoundT7Symbol => Entry.FoundT7Symbol;

    /// <summary>The matched symbol shows red when it isn't the one the entry is set to.</summary>
    public bool Mismatch => Entry.FoundT7Symbol != Entry.T7Symbol;

    /// <summary>Short name, at most 4 characters ("Maximum symbolname length = 4!").</summary>
    public string Symbol
    {
        get => Entry.Symbol;
        set
        {
            if (IsReadOnly || value.Length > 4) return;
            Entry.Symbol = value;
            OnPropertyChanged();
        }
    }

    /// <summary>The ID: 00 unsigned integer, 01 signed integer, 04 unsigned byte, 05 signed byte.</summary>
    public int IdIndex
    {
        get => SidInfoViewModel.Ids.FindIndex(i => i.StartsWith(Entry.Value));
        set
        {
            if (IsReadOnly || value < 0) return;
            Entry.Value = SidInfoViewModel.Ids[value][..2];
            OnPropertyChanged();
        }
    }

    /// <summary>The T7 symbol; choosing one fills in the code, address, description, matched symbol and ID.</summary>
    public string T7Symbol
    {
        get => Entry.T7Symbol;
        set
        {
            if (IsReadOnly || value == Entry.T7Symbol || m_bin.FindAny(value) is not { } sh) return;
            SidInfo.Assign(Entry, sh);
            OnPropertyChanged("");
        }
    }
}

/// <summary>frmSIDInformation: the SID display's entries grouped by mode; Ok writes them into the binary.</summary>
public class SidInfoViewModel
{
    public static readonly List<string> Ids = ["00 Unsigned integer", "01 Signed integer", "02", "03", "04 Unsigned byte", "05 Signed byte"];

    public T7Binary Binary { get; }
    public ObservableCollection<SidRow> Rows { get; } = [];
    public DataGridCollectionView View { get; }
    public IReadOnlyList<string> SymbolNames { get; }
    public IReadOnlyList<string> IdNames => Ids;

    public SidInfoViewModel(T7Binary bin, List<SIDIHelper> rows)
    {
        Binary = bin;
        Load(rows);
        View = new DataGridCollectionView(Rows);
        View.GroupDescriptions.Add(new DataGridPathGroupDescription(nameof(SidRow.ModeDescr)));
        SymbolNames = SidInfo.Choices(bin).Select(s => s.SmartVarname).Distinct().ToList();
    }

    private void Load(IEnumerable<SIDIHelper> rows)
    {
        Rows.Clear();
        foreach (SIDIHelper h in rows) Rows.Add(new SidRow(Binary, h));
    }

    public void Export(string file) => SidInfo.Export(Binary, Rows.Select(r => r.Entry), file);

    /// <summary>False when the file has another number of rows ("Unable to import SIDi settings with a different length!").</summary>
    public bool Import(string file)
    {
        if (SidInfo.Import(Binary, file, Rows.Count) is not { } rows) return false;
        Load(rows);
        return true;
    }

    public void Save(bool autoFixFooter) => SidInfo.Write(Binary, Rows.Select(r => r.Entry).ToList(), autoFixFooter);
}

/// <summary>The matched symbol's red cell when it differs from the entry's symbol.</summary>
public static class SidBrushes
{
    public static readonly Avalonia.Data.Converters.FuncValueConverter<bool, Avalonia.Media.IBrush?> Mismatch =
        new(m => m ? new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Avalonia.Media.Color.FromRgb(255, 128, 128)) : null);
}
