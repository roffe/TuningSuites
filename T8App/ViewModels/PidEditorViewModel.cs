using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Collections;
using Avalonia.Data;
using CommonSuite;
using CommunityToolkit.Mvvm.ComponentModel;
using T8SuitePro;

namespace T8App.ViewModels;

/// <summary>A row of the PID or TEM editor (frmPID / frmTEM): a table entry and the details of its symbol.</summary>
public partial class PidRow : ObservableObject
{
    private readonly PidEditorViewModel m_owner;
    private SymbolHelper? m_symbol;

    public PidHelper Entry { get; }

    public PidRow(PidEditorViewModel owner, PidHelper entry)
    {
        m_owner = owner;
        Entry = entry;
        m_symbol = owner.SymbolWithIndex(entry.SymbolIndex);
    }

    /// <summary>The TEM table's "OFF" row (onEditAttempt).</summary>
    public bool CanEdit => !Entry.IsProtected;

    public int Number => Entry.Index;

    /// <summary>The PID, 4 hex digits (onEditValidation: "16-bit hex!").</summary>
    public string Pid
    {
        get => Entry.PID;
        set
        {
            if (!int.TryParse(value, NumberStyles.HexNumber, null, out int pid)) throw new DataValidationException("16-bit hex!");
            if (pid is < 0 or > 0xFFFF) throw new DataValidationException("16-bit hex!");
            Entry.PID = pid.ToString("X4");
            OnPropertyChanged("");
        }
    }

    /// <summary>The TEM label, 1 to 4 printable characters.</summary>
    public string Label
    {
        get => Entry.PID;
        set
        {
            if (!CanEdit) return;
            if (value.Length is 0 or > 4) throw new DataValidationException("Enter a name of length 1 to 4!");
            if (value.Any(c => c is < (char)0x20 or >= (char)0x7f)) throw new DataValidationException("No special characters!");
            Entry.PID = value;
            OnPropertyChanged("");
        }
    }

    public int Read
    {
        get => Entry.ReadFlag;
        set
        {
            if (value < 0) return;
            Entry.ReadFlag = value;
            OnPropertyChanged("");
        }
    }

    public int Write
    {
        get => Entry.WriteFlag;
        set
        {
            if (value < 0) return;
            Entry.WriteFlag = value;
            OnPropertyChanged("");
        }
    }

    /// <summary>The symbol by name (the lookup's display); choosing one sets the index and refreshes description, type, address and size.</summary>
    public string Symbol
    {
        get => m_symbol?.SmartVarname ?? "";
        set
        {
            if (!CanEdit || value == Symbol || !m_owner.SymbolNames.Contains(value) || m_owner.Find(value) is not { } sh) return;
            m_symbol = sh;
            Entry.SymbolIndex = sh.Symbol_number_ECU;
            OnPropertyChanged("");
        }
    }

    public string Description => CanEdit ? m_symbol?.Description ?? "" : "This message is displayed in idle mode (No data is read)";

    private int Address => m_symbol?.Internal_address ?? 0;
    private int Size => m_symbol?.Length ?? 0;
    private int Type => m_symbol?.Symbol_type ?? 0;

    public string AddressText => CanEdit ? Address.ToString("X6") : "";
    public string SizeText => CanEdit ? Size.ToString() : "";
    public string TypeText => CanEdit ? Type.ToString("X2") : "";

    // onDrawCell: OrangeRed for what the ECU can't use, yellow (TEM) when the symbol's type and size disagree; never on "OFF"
    public bool PidFault => Entry.PID.Length is 0 or > 4;
    public bool LabelFault => CanEdit && Entry.PID.Length is 0 or > 4;
    public bool ReadFault => Read > 2;
    private bool FlashWrite => Address < 0x100000 && Write != 0;
    public bool WriteFault => FlashWrite || Write > 2;
    public bool AddressFault => !m_owner.IsTem && FlashWrite;
    public bool SizeFault => CanEdit && (m_owner.IsTem ? Size < 1 : Size is < 1 or > 7);
    public bool SymbolFault => SizeFault || CanEdit && Entry.SymbolIndex is < 1 or > 20000;
    public bool TypeFault => m_owner.IsTem && SizeFault;

    /// <summary>Only part of the symbol will show: a byte not 1 long, a long not 4, a bitfield over 2, a word not 2, or an unknown flag.</summary>
    public bool Mismatch
    {
        get
        {
            if (!m_owner.IsTem || !CanEdit || Size < 1) return false;
            int t = Type & ~0x23;
            return (t & 0x04) > 0 && Size != 1 || (t & 0x48) > 0 && Size != 4 || (t & 0x10) > 0 && Size > 2 || t == 0 && Size != 2 || (t & 0x80) > 0;
        }
    }
}

/// <summary>
/// Actions → PID editor / TEM editor (frmPID / frmTEM): a copy of the open file's table; Ok writes it back (T8Binary.WritePids /
/// WriteTems). The symbol lookup lists what the table can use plus the symbols it already uses.
/// </summary>
public class PidEditorViewModel
{
    public static IReadOnlyList<string> Flags { get; } = ["Disabled", "Enabled", "Secured", "Unknown"];

    private readonly T8Binary m_bin;
    private readonly Dictionary<int, SymbolHelper> m_byIndex;

    public bool IsTem { get; }
    public PidCollection Table { get; }
    public List<PidRow> Rows { get; }
    public DataGridCollectionView View { get; }
    public List<string> SymbolNames { get; }
    public string Title { get; }

    public PidEditorViewModel(T8Binary bin, bool tem)
    {
        m_bin = bin;
        IsTem = tem;
        Table = (tem ? bin.Tems : bin.Pids)?.CopyOf() ?? new PidCollection();
        var symbols = bin.Symbols.Cast<SymbolHelper>().ToList();
        m_byIndex = symbols.GroupBy(s => s.Symbol_number_ECU).ToDictionary(g => g.Key, g => g.First());
        // PopulateSymbols: PIDs take 1 to 7 bytes; TEM takes anything with an address that isn't an empty typedef
        var usable = symbols.Where(s => tem ? (s.Symbol_type != 0x20 || s.Length > 0) && s.Internal_address > 0 : s.Length is > 0 and < 8);
        var used = Table.Cast<PidHelper>().Where(p => !p.IsProtected).Select(p => SymbolWithIndex(p.SymbolIndex)).OfType<SymbolHelper>();
        SymbolNames = usable.Concat(used).Select(s => s.SmartVarname).Distinct().ToList();
        Rows = Table.Cast<PidHelper>().Select(p => new PidRow(this, p)).ToList();
        View = new DataGridCollectionView(Rows);
        Title = $"{(tem ? "TEM" : "PID")} editor ({Table.Count} items)";
    }

    public SymbolHelper? SymbolWithIndex(int index) => m_byIndex.GetValueOrDefault(index);

    public SymbolHelper? Find(string name) => m_bin.Find(name);

    /// <summary>The grid's find panel: rows whose PID / label, symbol or description contain the text.</summary>
    public void Filter(string text)
    {
        View.Filter = text == "" ? null : o => o is PidRow r && new[] { r.Pid, r.Symbol, r.Description }.Any(v => v.Contains(text, StringComparison.OrdinalIgnoreCase));
        View.Refresh();
    }
}
