using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommonSuite;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using T7;
using TrionicCANLib.Checksum;

namespace T7App.ViewModels;

public record FaultCode(string Code, string Description);

/// <summary>The ECU side of the main window (frmMain's CAN flasher, SRAM and fault code features).</summary>
public partial class MainWindowViewModel
{
    public T7Ecu Ecu { get; } = new();

    /// <summary>m_RealtimeConnectedToECU.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectCaption))]
    private bool _isConnected;

    /// <summary>The status bar's ECU field (SetCANStatus).</summary>
    [ObservableProperty]
    private string _canStatus = "";

    /// <summary>The imported SRAM snapshot (.RAM), "SRAM: name" in the status bar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SramFileText), nameof(HasSramFile))]
    private string? _sramFile;

    public string SramFileText => SramFile == null ? "" : "SRAM: " + Path.GetFileNameWithoutExtension(SramFile);
    public bool HasSramFile => SramFile != null && File.Exists(SramFile);

    public string ConnectCaption => IsConnected ? "Disconnect ECU" : "Connect ECU";

    private DispatcherTimer? m_sramTimer;

    private void InitEcu()
    {
        Ecu.Info += (_, e) => Dispatcher.UIThread.Post(() => ProgressText = e.Info);
        Ecu.Progress += (_, p) => Dispatcher.UIThread.Post(() => ProgressText = p >= 100 ? "Done" : $"{p} %");
        RestartSramTimer();
    }

    /// <summary>RealtimeCheckAndConnect.</summary>
    public async Task<bool> EnsureConnectedAsync()
    {
        if (Ecu.IsConnected) return true;
        CanStatus = "Initializing CANbus interface";
        try
        {
            IsConnected = await Ecu.ConnectAsync(Settings);
        }
        catch (InvalidOperationException e)
        {
            CanStatus = "";
            ShowInfo(e.Message);
            return false;
        }
        CanStatus = IsConnected ? "Connected" : "Failed to start KWP session";
        return IsConnected;
    }

    [RelayCommand]
    private async Task ConnectDisconnect()
    {
        if (Ecu.IsConnected)
        {
            await Ecu.DisconnectAsync();
            IsConnected = false;
            CanStatus = "";
            foreach (MapViewerViewModel v in Viewers.OfType<MapViewerViewModel>()) v.OnlineMode = false;
        }
        else
        {
            await EnsureConnectedAsync();
        }
    }

    // ---- SRAM maps ----

    /// <summary>A map that only lives in SRAM: read it from the ECU (connecting first) and show it.</summary>
    private async Task OpenSramSymbolAsync(T7Binary bin, SymbolHelper sh)
    {
        if (!await EnsureConnectedAsync())
        {
            ShowInfo("An active CAN bus connection is needed to get data from the ECU");
            return;
        }
        byte[] data = await Ecu.ReadMapAsync(sh);
        if (MapViewerViewModel.Create(this, bin, sh, data, sram: true) is { } viewer)
        {
            viewer.OnlineMode = true;
            ShowDocument(viewer);
        }
    }

    /// <summary>The viewer's Read from ECU: every viewer of the map shows the ECU's data.</summary>
    public async Task ReadMapFromEcuAsync(MapViewerViewModel viewer)
    {
        if (!await EnsureConnectedAsync())
        {
            ShowInfo("An active CAN bus connection is needed to get data from the ECU");
            return;
        }
        byte[] data = await Ecu.ReadMapAsync(viewer.Symbol);
        foreach (MapViewerViewModel v in Viewers.OfType<MapViewerViewModel>().Where(v => v.MapName == viewer.MapName && !v.IsReadOnly))
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
        if (!await Ecu.WriteMapAsync(viewer.Symbol, viewer.Map.ToBytes()))
            ShowInfo($"The ECU did not accept {viewer.MapName}. Writing to SRAM needs an open binary in the ECU.");
    }

    /// <summary>AutoUpdateSRAMViewers: online viewers without edits re-read every AutoUpdateInterval seconds.</summary>
    public void RestartSramTimer()
    {
        m_sramTimer?.Stop();
        if (!Settings.AutoUpdateSRAMViewers) return;
        m_sramTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Clamp(Settings.AutoUpdateInterval, 5, 60)) };
        m_sramTimer.Tick += async (_, _) =>
        {
            if (!Ecu.IsConnected) return;
            foreach (MapViewerViewModel v in Viewers.OfType<MapViewerViewModel>().Where(v => v.OnlineMode && !v.Map.Mutated).ToList())
                await ReadMapFromEcuAsync(v);
        };
        m_sramTimer.Start();
    }

    // ---- flashing ----

    private async Task DisconnectRealtimeAsync()
    {
        if (!Ecu.IsConnected) return;
        await Ecu.DisconnectAsync();
        IsConnected = false;
        CanStatus = "";
    }

    /// <summary>Read ECU: the flash into a file; the result is the library's own message.</summary>
    public async Task ReadEcuAsync(string file)
    {
        await DisconnectRealtimeAsync();
        IsBusy = true;
        CanStatus = "Initializing CANbus interface";
        try
        {
            var (ok, message) = await Ecu.ReadFlashAsync(Settings, file);
            ShowInfo(ok ? $"Download done: {file}" : message);
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
    /// Flash current file to ECU. Unlike T7Suite, a checksum that doesn't verify is fixed (AutoChecksum) or offered to be
    /// fixed first, and unsaved map changes are pointed out; the file on disk is what gets flashed.
    /// </summary>
    public async Task FlashEcuAsync(Func<string, Task<bool>> askYesNo)
    {
        if (Binary is not { } bin || !File.Exists(bin.FileName))
        {
            ShowInfo("No file has been loaded");
            return;
        }
        if (Viewers.OfType<MapViewerViewModel>().Any(v => v.FileName == bin.FileName && v.Map.Mutated)
            && !await askYesNo("Some maps have changes that are not saved to the file and will not be flashed. Flash the file as it is?"))
            return;
        if (bin.VerifyChecksum() != ChecksumResult.Ok)
        {
            if (!Settings.AutoChecksum && !await askYesNo("Checksums did not verify ok, do you want to recalculate and update the checksums?"))
                return;
            bin.UpdateChecksum(Settings.AutoFixFooter);
        }
        await DisconnectRealtimeAsync();
        IsBusy = true;
        CanStatus = "Initializing CANbus interface";
        try
        {
            var (ok, message) = await Ecu.WriteFlashAsync(Settings, bin.FileName);
            ShowInfo(ok ? "Flash sequence done" : message);
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

    /// <summary>Get SRAM snapshot: SRAM&lt;time&gt;.RAM next to the bin, or Snapshots/Snapshot&lt;time&gt;.RAM in a project.</summary>
    public string SnapshotFileName()
    {
        DateTime now = DateTime.Now;
        if (Project is { } project)
        {
            string dir = Path.Combine(project.Dir, "Snapshots");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "Snapshot" + now.ToString("MMddyyyyHHmmss") + ".RAM");
        }
        string binDir = Binary != null ? Path.GetDirectoryName(Binary.FileName)! : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(binDir, "SRAM" + now.ToString("yyyyMMddHHmmssfff") + ".RAM");
    }

    [RelayCommand]
    private async Task GetSramSnapshot()
    {
        await DisconnectRealtimeAsync();
        string file = SnapshotFileName();
        IsBusy = true;
        try
        {
            if (await Ecu.SnapshotAsync(Settings, file)) ShowInfo("Snapshot downloaded and saved to: " + file);
            else ShowInfo("An active CAN bus connection is needed to read a sram snapshot");
        }
        catch (InvalidOperationException e)
        {
            ShowInfo(e.Message);
        }
        finally
        {
            IsBusy = false;
            ProgressText = "";
        }
    }

    // ---- fault codes ----

    private Dictionary<string, DTCDescription>? m_dtcCatalog;

    /// <summary>Get fault codes (OBDII): obdFaults from SRAM, with descriptions. Codes without one are listed too (T7Suite hid them).</summary>
    public async Task<List<FaultCode>?> ReadFaultCodesAsync()
    {
        if (!await EnsureConnectedAsync())
        {
            ShowInfo("An active CAN bus connection is needed to read faultcodes");
            return null;
        }
        if (Binary?.Find("obdFaults") is not { } obdFaults)
        {
            ShowInfo("Cannot find symbolnumber for symbol obdFaults, ECU binary must be loaded");
            return null;
        }
        m_dtcCatalog ??= DtcCatalog.Load();
        string[] codes = await Ecu.ReadFaultCodesAsync(obdFaults);
        return codes.Distinct().Select(c => new FaultCode(c, m_dtcCatalog.TryGetValue(c, out var d) ? d.Description : "")).ToList();
    }

    public async Task<List<FaultCode>?> ClearFaultCodeAsync(string code)
    {
        if (!await EnsureConnectedAsync()) return null;
        await Ecu.ClearFaultCodeAsync(code);
        return await ReadFaultCodesAsync();
    }

    [RelayCommand]
    private async Task ClearDtcAndKnockCounters()
    {
        if (await EnsureConnectedAsync()) await Ecu.ClearAllFaultCodesAsync();
    }

    // ---- SRAM snapshot files ----

    public void ImportSramSnapshot(string file) => SramFile = file;

    /// <summary>Read from SRAM file: the symbol's bytes at its SRAM address in the snapshot, as "SRAM Symbol: name [file]".</summary>
    public void OpenFromSramFile(SymbolHelper sh)
    {
        if (Binary is not { } bin || SramFile is not { } file || !File.Exists(file)) return;
        byte[] ram = File.ReadAllBytes(file);
        if (ram.Length == 0) return;
        var data = new byte[sh.Length];
        int start = (int)(sh.Start_address & 0xFFFF);
        for (int i = 0; i < data.Length; i++) data[i] = ram[(start + i) % ram.Length];
        string title = $"SRAM Symbol: {sh.SmartVarname} [{Path.GetFileName(file)}]";
        if (MapViewerViewModel.Create(this, bin, sh, data, sram: true, title: title) is { } viewer)
        {
            viewer.OnlineMode = true;
            ShowDocument(viewer);
        }
    }

    // ---- synchronize ----

    /// <summary>Synchronize to binary: every calibration symbol in SRAM into the file, then the checksum.</summary>
    public async Task SyncToBinaryAsync()
    {
        if (Binary is not { } bin) return;
        if (!await EnsureConnectedAsync())
        {
            ShowInfo("An active CAN bus connection is needed to get data from the ECU");
            return;
        }
        int before = TransactionLog?.TransCollection.Count ?? 0;
        foreach (SymbolHelper sh in SyncSymbols(bin))
        {
            ProgressText = "Sync from ECU: " + sh.SmartVarname;
            byte[] data = await Ecu.ReadMapAsync(sh);
            bin.WriteData((int)bin.AddressOf(sh), data, TransactionLog);
        }
        bin.UpdateChecksum(Settings.AutoFixFooter);
        TransactionsAdded(before);
        RefreshViewers(bin.FileName);
        ProgressText = "";
    }

    /// <summary>Synchronize to ECU: every calibration symbol of the file into SRAM.</summary>
    public async Task SyncToEcuAsync()
    {
        if (Binary is not { } bin) return;
        if (!await EnsureConnectedAsync())
        {
            ShowInfo("An active CAN bus connection is needed to get data from the ECU");
            return;
        }
        foreach (SymbolHelper sh in SyncSymbols(bin))
        {
            ProgressText = "Sync to ECU: " + sh.SmartVarname;
            await Ecu.WriteMapAsync(sh, bin.Read((int)bin.AddressOf(sh), sh.Length));
        }
        ProgressText = "";
    }

    private static IEnumerable<SymbolHelper> SyncSymbols(T7Binary bin) =>
        bin.Symbols.Cast<SymbolHelper>().Where(sh => sh.Start_address > 0x80000 && T7Compare.IsCalibration(sh.SmartVarname)
            && bin.AddressOf(sh) is > 0 and < 0x80000).ToList();
}
