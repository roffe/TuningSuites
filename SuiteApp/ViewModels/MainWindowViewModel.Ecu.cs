using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommonSuite;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrionicCANLib.Checksum;

namespace SuiteApp.ViewModels;

public record FaultCode(string Code, string Description);

/// <summary>
/// The ECU side both suites have on top of their own session (T7Ecu over KWP, T8Ecu over GMLAN): the connection state, the map
/// viewers' Read from / Save to ECU and their auto update, maps that only live in SRAM, .RAM snapshot files, the flasher's busy
/// state and fault code descriptions. What goes over the wire is the suite's.
/// </summary>
public abstract partial class MainWindowViewModel
{
    /// <summary>m_RealtimeConnectedToECU.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectCaption))]
    private bool _isConnected;

    public string ConnectCaption => IsConnected ? "Disconnect ECU" : "Connect ECU";

    /// <summary>The imported SRAM snapshot (.RAM), "SRAM: name" in the status bar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSramFile))]
    private string? _sramFile;

    partial void OnSramFileChanged(string? value) => SramFileText = value == null ? "" : "SRAM: " + Path.GetFileNameWithoutExtension(value);

    public bool HasSramFile => SramFile != null && File.Exists(SramFile);

    // ---- what the suite's session does ----

    /// <summary>Opens the realtime session: the status bar's text, null when it didn't open. Throws a message when no adapter is set.</summary>
    protected abstract Task<string?> ConnectEcuAsync();

    /// <summary>The status bar's text when the session didn't open.</summary>
    protected abstract string ConnectFailedText { get; }

    protected abstract Task DisconnectEcuAsync();

    /// <summary>The symbol's bytes from the ECU's SRAM, null when the read failed.</summary>
    protected abstract Task<byte[]?> ReadEcuMapAsync(SymbolHelper sh);

    /// <summary>The bytes into the ECU's SRAM; false when the ECU didn't accept them.</summary>
    protected abstract Task<bool> WriteEcuMapAsync(SymbolHelper sh, byte[] data);

    /// <summary>Why the ECU didn't take a map.</summary>
    protected virtual string WriteRefusedText(string map) => $"The ECU did not accept {map}.";

    /// <summary>Viewers the auto update re-reads: online ones without edits.</summary>
    protected virtual bool AutoUpdates(MapViewerViewModel viewer) => viewer.OnlineMode && !viewer.Map.Mutated;

    /// <summary>Read ECU: the flash into a file.</summary>
    public abstract Task ReadEcuAsync(string file);

    /// <summary>Flash current file to ECU (askYesNo for the questions on the way).</summary>
    public abstract Task FlashEcuAsync(Func<string, Task<bool>> askYesNo);

    /// <summary>Get fault codes (OBDII): the ECU's codes with descriptions, null when there was nothing to show.</summary>
    public abstract Task<List<FaultCode>?> ReadFaultCodesAsync();

    /// <summary>The fault code window's Clear: the code (or all of them, where the suite can only do that), then the codes again.</summary>
    public abstract Task<List<FaultCode>?> ClearFaultCodeAsync(string code);

    // ---- the connection ----

    /// <summary>RealtimeCheckAndConnect.</summary>
    public async Task<bool> EnsureConnectedAsync()
    {
        if (EcuConnected) return true;
        CanStatus = "Initializing CANbus interface";
        string? status;
        try
        {
            status = await ConnectEcuAsync();
        }
        catch (InvalidOperationException e)
        {
            CanStatus = "";
            ShowInfo(e.Message);
            return false;
        }
        IsConnected = status != null;
        CanStatus = status ?? ConnectFailedText;
        return IsConnected;
    }

    [RelayCommand]
    private async Task ConnectDisconnect()
    {
        if (EcuConnected)
        {
            await DisconnectEcuAsync();
            IsConnected = false;
            CanStatus = "";
            foreach (MapViewerViewModel v in Viewers.OfType<MapViewerViewModel>()) v.OnlineMode = false;
        }
        else
        {
            await EnsureConnectedAsync();
        }
    }

    protected async Task DisconnectRealtimeAsync()
    {
        if (!EcuConnected) return;
        await DisconnectEcuAsync();
        IsConnected = false;
        CanStatus = "";
    }

    // ---- SRAM compares ----

    /// <summary>An SRAM snapshot, laid out by the open bin's symbols: "SRAM Symbol" viewers.</summary>
    private CompareSide SramSide(SuiteBinary bin, string file)
    {
        byte[] ram = File.ReadAllBytes(file);
        return new(file, name => bin.FindAny(name) is { } sh ? SuiteCompare.ReadSram(ram, sh.Start_address, sh.Length) : null,
            name => { if (bin.FindAny(name) is { } sh) OpenFromSramFile(sh, file); });
    }

    /// <summary>Compare to SRAM snapshot: "SRAM &lt;&gt; BIN Compare results", the bin's map and the snapshot's per symbol.</summary>
    public async Task CompareToSramAsync(string file)
    {
        if (Binary is not { } bin) return;
        List<CompareRow> rows = await Task.Run(() => SuiteCompare.CompareToSram(bin, File.ReadAllBytes(file)));
        string name = Path.GetFileName(file);
        ShowDocument(new CompareResultsViewModel(this, bin, CompareSide.Bin(this, bin), SramSide(bin, file), rows,
            $"SRAM <> BIN Compare results: {name}", $"SRAM symbol difference: {{0}} [{name}]"));
    }

    /// <summary>Compare SRAM snapshots: both snapshots' maps per symbol.</summary>
    public async Task CompareSramAsync(string file1, string file2)
    {
        if (Binary is not { } bin) return;
        List<CompareRow> rows = await Task.Run(() =>
            SuiteCompare.CompareSram(bin, File.ReadAllBytes(file1), File.ReadAllBytes(file2)));
        string a = Path.GetFileName(file1), b = Path.GetFileName(file2);
        ShowDocument(new CompareResultsViewModel(this, bin, SramSide(bin, file1), SramSide(bin, file2), rows,
            $"SRAM compare results: {a} {b}", $"SRAM symbol difference: {{0}} [{a} vs {b}]"));
    }

    // ---- SRAM maps ----

    /// <summary>T5Suite: while connected every map shows the ECU's SRAM, and a viewer's Save writes SRAM and the file in one go.</summary>
    protected virtual bool OnlineMapsFromEcu => false;

    public bool SaveWritesEcu => OnlineMapsFromEcu && EcuConnected;

    /// <summary>A map that only lives in SRAM; T7Suite / T8Suite connected and read it.</summary>
    protected virtual Task OpenSramOnlyAsync(SuiteBinary bin, SymbolHelper sh) => OpenSramSymbolAsync(bin, sh);

    /// <summary>A map that only lives in SRAM: read it from the ECU (connecting first) and show it.</summary>
    protected async Task OpenSramSymbolAsync(SuiteBinary bin, SymbolHelper sh)
    {
        if (!await EnsureConnectedAsync())
        {
            ShowInfo("An active CAN bus connection is needed to get data from the ECU");
            return;
        }
        if (await ReadEcuMapAsync(sh) is not { } data)
        {
            ProgressText = "Could not read SRAM";
            return;
        }
        if (MapViewerViewModel.Create(this, bin, sh, data, sram: true) is { } viewer)
        {
            viewer.OnlineMode = true;
            ShowDocument(viewer);
        }
    }

    /// <summary>The viewer's Read from ECU (connecting first): every viewer of the map shows the ECU's data.</summary>
    public async Task ReadMapFromEcuAsync(MapViewerViewModel viewer)
    {
        if (!await EnsureConnectedAsync())
        {
            ShowInfo("An active CAN bus connection is needed to get data from the ECU");
            return;
        }
        if (await ReadEcuMapAsync(viewer.Symbol) is not { } data)
        {
            ProgressText = "Could not read SRAM";
            return;
        }
        // the same file's viewers of the map; another one with unsaved edits keeps them
        foreach (MapViewerViewModel v in Viewers.OfType<MapViewerViewModel>()
                     .Where(v => v.MapName == viewer.MapName && !v.IsReadOnly && v.FileName == viewer.FileName && (v == viewer || !v.Map.Mutated)))
        {
            if (data.Length == v.Map.Count * (v.Map.SixteenBit ? 2 : 1)) v.Map.Load(data);
            v.OnlineMode = true;
        }
    }

    /// <summary>The viewer's Save to ECU (WriteMapToSRAM).</summary>
    public async Task WriteMapToEcuAsync(MapViewerViewModel viewer)
    {
        if (!await EnsureConnectedAsync())
        {
            ShowInfo("An active CAN bus connection is needed to write data to the ECU");
            return;
        }
        if (!await WriteEcuMapAsync(viewer.Symbol, viewer.Map.ToBytes())) ShowInfo(WriteRefusedText(viewer.MapName));
    }

    private DispatcherTimer? m_sramTimer;

    /// <summary>AutoUpdateSRAMViewers: while connected, the viewers the suite updates re-read every AutoUpdateInterval seconds.</summary>
    public void RestartSramTimer()
    {
        m_sramTimer?.Stop();
        if (!Settings.AutoUpdateSRAMViewers) return;
        m_sramTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Clamp(Settings.AutoUpdateInterval, 5, 60)) };
        m_sramTimer.Tick += async (_, _) =>
        {
            if (!EcuConnected) return;
            foreach (MapViewerViewModel v in Viewers.OfType<MapViewerViewModel>().Where(AutoUpdates).ToList())
                await ReadMapFromEcuAsync(v);
        };
        m_sramTimer.Start();
    }

    // ---- the flasher ----

    /// <summary>
    /// A flasher operation (Read ECU, Flash, Recover, snapshot): the realtime connection closed first, busy while it runs, the
    /// adapter's complaint shown, the status cleared afterwards.
    /// </summary>
    protected async Task RunFlasherAsync(Func<Task> operation)
    {
        await DisconnectRealtimeAsync();
        IsBusy = true;
        CanStatus = "Initializing CANbus interface";
        try
        {
            await operation();
        }
        catch (InvalidOperationException e)
        {
            ShowInfo(e.Message);
        }
        finally
        {
            IsBusy = false;
            CanStatus = "";
            ProgressText = "";
        }
    }

    /// <summary>
    /// Flash current file to ECU flashes the file on disk: unsaved map changes are pointed out, and a checksum that doesn't
    /// verify is fixed (Auto update checksum) or offered to be fixed first. Neither suite checked either. Null: don't flash.
    /// </summary>
    protected async Task<SuiteBinary?> FlashableBinaryAsync(Func<string, Task<bool>> askYesNo)
    {
        if (Binary is not { } bin || !File.Exists(bin.FileName))
        {
            ShowInfo("No file has been loaded");
            return null;
        }
        if (Viewers.OfType<MapViewerViewModel>().Any(v => v.FileName == bin.FileName && v.Map.Mutated)
            && !await askYesNo("Some maps have changes that are not saved to the file and will not be flashed. Flash the file as it is?"))
            return null;
        if (bin.VerifyChecksum() != ChecksumResult.Ok)
        {
            if (!Settings.AutoChecksum && !await askYesNo("Checksums did not verify ok, do you want to recalculate and update the checksums?"))
                return null;
            bin.UpdateChecksum();
        }
        return bin;
    }

    // ---- fault codes ----

    private Dictionary<string, DTCDescription>? m_dtcCatalog;

    /// <summary>The codes with frmFaultcodes' descriptions (DTC_*.xml next to the program); codes without one are listed too.</summary>
    protected List<FaultCode> Describe(IEnumerable<string> codes)
    {
        m_dtcCatalog ??= DtcCatalog.Load();
        return codes.Distinct().Select(c => new FaultCode(c, m_dtcCatalog.TryGetValue(c, out var d) ? d.Description : "")).ToList();
    }

    // ---- SRAM snapshot files ----

    public void ImportSramSnapshot(string file) => SramFile = file;

    /// <summary>Read from SRAM file: the symbol's bytes at its SRAM address in the snapshot, as "SRAM Symbol: name [file]".</summary>
    public void OpenFromSramFile(SymbolHelper sh, string? file = null)
    {
        file ??= SramFile;
        if (Binary is not { } bin || file == null || !File.Exists(file)) return;
        byte[] ram = File.ReadAllBytes(file);
        if (ram.Length == 0) return;
        byte[] data = SuiteCompare.ReadSram(ram, sh.Start_address, sh.Length);
        string title = $"SRAM Symbol: {sh.SmartVarname} [{Path.GetFileName(file)}]";
        if (MapViewerViewModel.Create(this, bin, sh, data, sram: true, title: title) is { } viewer)
        {
            viewer.OnlineMode = true;
            ShowDocument(viewer);
        }
    }
}
