using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommonSuite;
using SuiteApp.ViewModels;
using T8SuitePro;
using TrionicCANLib.API;
using TrionicCANLib.Checksum;
using TrionicCANLib.Firmware;

namespace T8App.ViewModels;

/// <summary>T8Suite's main window (Form1): T8 binaries on the shared window; T8Suite's own features come chunk by chunk.</summary>
public partial class T8MainWindowViewModel : MainWindowViewModel
{
    public T8MainWindowViewModel() : base("T8SuitePro", "T8Suite", new T8SuiteRegistry())
    {
        // "Symbol: ...", "Adding symbol names: ", "Importing symbols"; "Idle" / "Completed" clear it
        Trionic8File.onProgress += (_, e) => Dispatcher.UIThread.Post(() => ProgressText = e.Info is "Idle" or "Completed" ? "" : e.Info);
    }

    protected override uint FileLength => FileT8.Length;

    // says why itself ("File has incorrect length: x", "File does not seem to be a Trionic 8 file: x")
    protected override bool IsValidFile(string path) => T8Binary.IsValidFile(path);

    protected override SuiteBinary OpenBinary(string path) => T8Binary.Open(path, Settings.MapDetectionActive);

    protected override string ReleaseTagPrefix => "T8suite_v";

    /// <summary>Form1.OpenFile ends with UpdateChecksum(file, AutoChecksum): checked on every open, the result in the status bar.</summary>
    protected override async Task OnOpenedAsync(SuiteBinary bin) => await CheckChecksumAsync(bin.FileName);

    /// <summary>Actions → Verify checksum: the same as on open (T8Suite showed no message, only the status bar).</summary>
    protected override async Task VerifyChecksumAsync()
    {
        if (Binary is { } bin) await CheckChecksumAsync(bin.FileName);
    }

    /// <summary>
    /// UpdateChecksum: with AutoChecksum (the default) a wrong checksum is corrected without asking, as T8Suite did; without it
    /// each failing layer asks (frmChecksum).
    /// </summary>
    private async Task CheckChecksumAsync(string file)
    {
        bool correct = Settings.AutoChecksum;
        ChecksumResult result = await Task.Run(() => ChecksumT8.VerifyChecksum(file, correct, (layer, inFile, real) =>
            UserPrompt.AskYesNo($"{layer}\n\nFile checksum: {inFile}\nActual checksum: {real}\n\nUpdate the checksum?", "Trionic checksum")));
        ChecksumText = result switch
        {
            ChecksumResult.Ok => "Checksum: OK",
            ChecksumResult.Layer1Failed => "Checksum: Layer 1 invalid",
            ChecksumResult.Layer2Failed => "Checksum: Layer 2 invalid",
            ChecksumResult.InvalidFileLength => "Checksum: no checksum area",
            _ => "Checksum: update failed",
        };
    }

    /// <summary>Create project (Form1 9132): car model = T8Header's CarDescription (empty, T8Suite never read it), "&lt;partnumber&gt; &lt;software version&gt;".</summary>
    protected override (string carModel, string projectName) ProjectDefaults(SuiteBinary bin)
    {
        var header = new T8Header();
        header.init(bin.FileName);
        return (header.CarDescription, header.PartNumber.Trim() + " " + header.SoftwareVersion.Trim());
    }

    /// <summary>gridViewSymbols' columns: name, length, user description, number, type; the addresses, bit mask and description hidden.</summary>
    public override IReadOnlyList<SymbolColumn> SymbolColumns { get; } =
    [
        new("Symbol name", nameof(SymbolHelper.Varname)), new("Length", nameof(SymbolHelper.Length)),
        new("User description", nameof(SymbolHelper.Userdescription)), new("Number", nameof(SymbolHelper.Symbol_number)),
        new("Type", nameof(SymbolHelper.Symbol_type)), new("Address", nameof(SymbolHelper.Flash_start_address), false),
        new("SRAM Address", nameof(SymbolHelper.Start_address), false), new("Bit mask", nameof(SymbolHelper.BitMask), false),
        new("Description", nameof(SymbolHelper.Description), false), new("Category", nameof(SymbolHelper.Category), false),
    ];

    /// <summary>gridViewSymbols' sort: the categories alphabetically, inside one length descending, then name.</summary>
    protected override IEnumerable<SymbolHelper> OrderSymbols(IEnumerable<SymbolHelper> symbols) =>
        symbols.OrderBy(s => s.Category, System.StringComparer.Ordinal).ThenByDescending(s => s.Length).ThenBy(s => s.Varname, System.StringComparer.Ordinal);

    public override bool ColorSymbolNames => false;

    private static readonly SymbolFilter AllSymbols = new("All symbols", _ => true);

    /// <summary>SetDefaultFilters: "Only symbols within binary" after every open, or "Only live-tuneable symbols".</summary>
    private static readonly SymbolFilter WithinBinary = new("Only symbols within binary", sh => sh.Length != 0 && sh.Flash_start_address < 0x100000);

    private static readonly SymbolFilter LiveTuneable = new("Only live-tuneable symbols", sh => sh.Length != 0 && sh.Start_address >= 0x100000);

    public override IReadOnlyList<SymbolFilter> SymbolFilters { get; } = [AllSymbols, WithinBinary, LiveTuneable];

    protected override SymbolFilter? DefaultSymbolFilter => WithinBinary;

    /// <summary>Bit mask symbols open the bit mask viewer (StartBitMaskViewer), which comes with the T8 tools.</summary>
    protected override bool OpenOther(SuiteBinary bin, SymbolHelper sh)
    {
        if (sh.BitMask <= 0) return false;
        ShowInfo($"{sh.SmartVarname} is a bit mask symbol; the bit mask viewer isn't ported yet");
        return true;
    }

    /// <summary>Actions → Firmware information (read only so far).</summary>
    public FirmwareInfo? FirmwareInfo() => Binary is { } bin && System.IO.File.Exists(bin.FileName) ? T8SuitePro.FirmwareInfo.Read(bin.FileName) : null;
}
