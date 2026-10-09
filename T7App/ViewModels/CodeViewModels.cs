using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AvaloniaEdit.Document;
using AvaloniaHex.Document;
using CommonSuite;
using CommunityToolkit.Mvvm.ComponentModel;
using T7;
using SuiteApp.ViewModels;

namespace T7App.ViewModels;

/// <summary>
/// "Disassembly: file" (ctrlDisassembler / AsmViewer): the .asm text, editable and saved back to its file, beside a read-only
/// hex view of the bin it came from (ctrlDisassembler's hexViewer1); the text caret and the hex view follow each other.
/// </summary>
public partial class DisassemblyViewModel(string file, byte[] binary, bool full = false, T7Binary? bin = null) : DocumentViewModel
{
    /// <summary>The hex pane's caret: offset and the symbol there (T7Suite's HexViewer showed it on its toolbar).</summary>
    [ObservableProperty]
    private string _hexStatus = "";

    public void HexCaretAt(ulong offset) => HexStatus = HexViewerViewModel.HexStatus(bin, offset, Binary.Length);

    public string FileName { get; } = file;

    public override string Title => "Disassembly: " + Path.GetFileName(FileName);

    public TextDocument Document { get; } = new(File.ReadAllText(file));

    /// <summary>The bin's bytes in memory (T7Suite copied the bin to a temp file it never deleted).</summary>
    public MemoryBinaryDocument Binary { get; } = new(binary, true);

    /// <summary>The full disassembly's "SSSSAAAA: ..." listing rather than the functions' "0xADDRESS\t..." one.</summary>
    public bool Full { get; } = full;

    public void Save() => File.WriteAllText(FileName, Document.Text);

    /// <summary>Text → hex (Caret_PositionChanged): the bytes of the instruction on a 1-based line, clamped to the bin; null without an address.</summary>
    public (ulong Start, ulong End)? LineBytes(int line)
    {
        if (line < 1 || line > Document.LineCount) return null;
        string text = Document.GetText(Document.GetLineByNumber(line));
        string? next = line < Document.LineCount ? Document.GetText(Document.GetLineByNumber(line + 1)) : null;
        if (InstructionRange(text, next) is not { } r || r.Start >= Binary.Length) return null;
        return (r.Start, Math.Min(r.End, Binary.Length));
    }

    /// <summary>Hex → text (hexViewer1_onSelectionChanged): where a byte address is in the listing, or null.</summary>
    public (int Offset, int Length)? FindAddress(ulong address) => FindAddress(Document.Text, (uint)address, Full);

    /// <summary>
    /// The address a listing line starts with: "0xADDRESS" (the word up to the tab) or the full listing's 8 hex digits and ':'.
    /// Labels ("LBL_...:", vector names) and blank lines have none.
    /// </summary>
    public static bool TryParseAddress(string line, out uint address)
    {
        address = 0;
        if (line.StartsWith("0x", StringComparison.Ordinal))
        {
            int end = 2;
            while (end < line.Length && !char.IsWhiteSpace(line[end])) end++;
            return uint.TryParse(line.AsSpan(2, end - 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out address);
        }
        return line.Length > 8 && line[8] == ':' &&
               uint.TryParse(line.AsSpan(0, 8), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out address);
    }

    /// <summary>
    /// The instruction on a line: from its address up to the next line's address, or 4 bytes on when the next line has none.
    /// T7Suite selected a negative length when the next address was lower; that falls back to 4 bytes too.
    /// </summary>
    public static (uint Start, uint End)? InstructionRange(string line, string? next)
    {
        if (!TryParseAddress(line, out uint start)) return null;
        uint end = next != null && TryParseAddress(next, out uint n) && n > start ? n : start + 4;
        return (start, end);
    }

    /// <summary>
    /// The first "0x%08X" of an address as a whole word, match case (T7Suite's search); in the full listing the "%08X:"
    /// that starts its line.
    /// </summary>
    public static (int Offset, int Length)? FindAddress(string text, uint address, bool full)
    {
        Match m = full
            ? Regex.Match(text, $"^{address:X8}:", RegexOptions.Multiline | RegexOptions.CultureInvariant)
            : Regex.Match(text, $@"\b0x{address:X8}\b", RegexOptions.CultureInvariant);
        return m.Success ? (m.Index, m.Length) : null;
    }
}

/// <summary>
/// "Hexviewer: file": the file's bytes, edited in place; saving copies &lt;file&gt;-backup&lt;ticks&gt; first and writes the bytes
/// as they are (no checksum update, as T7Suite). The status shows the symbol under the caret.
/// </summary>
public partial class HexViewerViewModel : DocumentViewModel
{
    private readonly T7Binary? m_bin;
    private readonly bool m_sram;

    public string FileName { get; }
    public MemoryBinaryDocument Document { get; }

    public override string Title => (m_sram ? "SRAM Hexviewer: " : "Hexviewer: ") + Path.GetFileName(FileName);

    [ObservableProperty] private bool _isModified;
    [ObservableProperty] private string _status = "";

    public HexViewerViewModel(string file, T7Binary? bin, bool sram = false)
    {
        FileName = file;
        m_bin = bin;
        m_sram = sram;
        Document = new MemoryBinaryDocument(File.ReadAllBytes(file));
        Document.Changed += (_, _) => IsModified = true;
    }

    /// <summary>The caret moved: "Ln / Col" and the symbol there (addresses wrap at the file size; SRAM files go by SRAM address).</summary>
    public void CaretAt(ulong offset) => Status = HexStatus(m_bin, offset, Document.Length, m_sram);

    /// <summary>"0xOFFSET    symbol": the symbol a byte belongs to (addresses wrap at the file size; SRAM files by SRAM address).</summary>
    public static string HexStatus(T7Binary? bin, ulong offset, ulong length, bool sram = false)
    {
        string symbol = "No symbol";
        if (bin != null && length > 0)
        {
            long a = (long)offset;
            SymbolHelper? sh = bin.Symbols.Cast<SymbolHelper>().FirstOrDefault(s =>
            {
                long start = sram ? s.Start_address : s.Flash_start_address;
                if (start <= 0 || s.Length <= 0) return false;
                start %= (long)length;
                return a >= start && a < start + s.Length && !s.SmartVarname.StartsWith("Pressure map");
            });
            if (sh != null) symbol = sh.SmartVarname;
        }
        return $"0x{offset:X6}    {symbol}";
    }

    public void Save()
    {
        File.Copy(FileName, FileName + "-backup" + DateTime.Now.Ticks, true);
        File.WriteAllBytes(FileName, Document.Memory.ToArray());
        IsModified = false;
    }

    /// <summary>Closing with changes: "Do you want to save changes?" Yes / No / Cancel.</summary>
    public override async System.Threading.Tasks.Task<bool> CanCloseAsync(MainWindowViewModel owner)
    {
        if (!IsModified || owner.AskYesNoCancel == null) return true;
        bool? save = await owner.AskYesNoCancel("Do you want to save changes?");
        if (save == null) return false;
        if (save == true) Save();
        return true;
    }
}
