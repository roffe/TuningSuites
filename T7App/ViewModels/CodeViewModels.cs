using System;
using System.IO;
using System.Linq;
using AvaloniaEdit.Document;
using AvaloniaHex.Document;
using CommonSuite;
using CommunityToolkit.Mvvm.ComponentModel;
using T7;

namespace T7App.ViewModels;

/// <summary>"Disassembly: file" (ctrlDisassembler / AsmViewer): the .asm text, editable and saved back to its file.</summary>
public class DisassemblyViewModel(string file) : DocumentViewModel
{
    public string FileName { get; } = file;

    public override string Title => "Disassembly: " + Path.GetFileName(FileName);

    public TextDocument Document { get; } = new(File.ReadAllText(file));

    public void Save() => File.WriteAllText(FileName, Document.Text);
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
    public void CaretAt(ulong offset)
    {
        string symbol = "No symbol";
        if (m_bin != null)
        {
            long a = (long)offset;
            SymbolHelper? sh = m_bin.Symbols.Cast<SymbolHelper>().FirstOrDefault(s =>
            {
                long start = m_sram ? s.Start_address : s.Flash_start_address;
                if (start <= 0 || s.Length <= 0) return false;
                start %= (long)Document.Length;
                return a >= start && a < start + s.Length && !s.SmartVarname.StartsWith("Pressure map");
            });
            if (sh != null) symbol = sh.SmartVarname;
        }
        Status = $"0x{offset:X6}    {symbol}";
    }

    public void Save()
    {
        File.Copy(FileName, FileName + "-backup" + DateTime.Now.Ticks, true);
        File.WriteAllBytes(FileName, Document.Memory.ToArray());
        IsModified = false;
    }
}
