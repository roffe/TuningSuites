using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommonSuite;
using CommunityToolkit.Mvvm.Input;
using SuiteApp.ViewModels;
using Trionic5Tools;
using TrionicCANLib.API;

namespace T5App.ViewModels;

/// <summary>The ECU side over T5Ecu: the "Online tuning" page and the CANBUS group of "ECU programming".</summary>
public partial class T5MainWindowViewModel
{
    public T5Ecu Ecu { get; } = new();

    protected override bool EcuConnected => Ecu.IsConnected;

    public override string? CloseBlocker => Ecu.IsFlashing ? "Wait until the ECU operation has finished before closing T5Suite." : null;

    public override void Shutdown() => Ecu.Dispose();

    private void InitEcu()
    {
        Ecu.Info += (_, e) => Dispatcher.UIThread.Post(() => ProgressText = e.Info);
        Ecu.Progress += (_, p) => Dispatcher.UIThread.Post(() => ProgressText = p >= 100 ? "Done" : $"{p} %");
    }

    private bool IsT52 => Binary is T5Binary { IsTrionic55: false };

    // "Connected: <software version>"; the ECU answered with another version than the bin's: said, but allowed
    protected override async Task<string?> ConnectEcuAsync()
    {
        if (await Ecu.ConnectAsync(Settings, IsT52) is not { } sw) return null;
        if (Binary is T5Binary bin && bin.File.GetSoftwareVersion().Trim() is var fileSw && fileSw != "" && !sw.EndsWith(fileSw) && !fileSw.EndsWith(sw))
            ShowInfo($"The ECU runs software {sw}, the open file is {fileSw}. Maps are read and written at the file's SRAM addresses.");
        _ = OfferSyncAsync();
        return "Connected: " + sw;
    }

    // ---- synchronization (frmSyncFileECU, SyncMaps) ----

    private static string SyncText(DateTime d) => d == DateTime.MinValue ? "none" : d.ToString("dd/MM/yyyy HH:mm:ss");

    /// <summary>
    /// The file's date (length - 0x1E0; an unstamped file gets "now", as T5Suite did) and the ECU's (SRAM 0x7FC0). An unstamped ECU
    /// reads as the oldest, so binary to ECU is proposed (T5Suite stamped it "now" and proposed ECU to binary).
    /// </summary>
    private async Task<(DateTime file, DateTime ecu)?> SyncDatesAsync()
    {
        if (T5 is not { } bin || !EcuConnected) return null;
        DateTime file = bin.SyncDate;
        if (file == T5Ecu.NoDate)
        {
            file = DateTime.Now;
            bin.File.SetMemorySyncDate(file);
        }
        DateTime ecu = await Ecu.ReadSyncDateAsync();
        return (file, ecu == T5Ecu.NoDate ? DateTime.MinValue : ecu);
    }

    /// <summary>On connect: "Data synchronization", the proposal from the newer date; Accept, Decline or Reverse.</summary>
    private async Task OfferSyncAsync()
    {
        if (await SyncDatesAsync() is not { } dates || dates.file == dates.ecu || AskButtons == null) return;
        var (file, ecu) = dates;
        bool toFile = ecu > file;
        int? choice = await AskButtons($"Timestamp binary: {SyncText(file)}\nTimestamp ECU: {SyncText(ecu)}\n\nProposed sync: {(toFile ? "ECU to binary" : "binary to ECU")}",
            "Data synchronization", ["Accept", "Decline", "Reverse"]);
        if (choice is 0 or 2) await SyncMapsAsync(choice == 0 ? toFile : !toFile, file, ecu);
    }

    /// <summary>Online tuning → Synchronize maps: the direction from the dates, no question.</summary>
    [RelayCommand]
    private async Task SynchronizeMaps()
    {
        if (await SyncDatesAsync() is not { } dates)
        {
            ShowInfo("No connection to ECU available");
            return;
        }
        var (file, ecu) = dates;
        if (file == ecu) ShowInfo("Synchronization not needed");
        else await SyncMapsAsync(ecu > file, file, ecu);
    }

    /// <summary>
    /// SyncMaps: every map in flash and SRAM read from the ECU and copied one way where it differs ("Sync: N%"); then the target's
    /// date is the source's. File writes get no transaction entries, as in T5Suite; the checksum follows Auto update checksum.
    /// </summary>
    private async Task SyncMapsAsync(bool toFile, DateTime file, DateTime ecu)
    {
        if (T5 is not { } bin) return;
        Project?.Logbook.WriteLogbookEntry(LogbookEntryType.SynchronizationStarted, toFile ? "ECU to binary" : "binary to ECU");
        var maps = bin.Symbols.Cast<SymbolHelper>().Where(sh => sh.Start_address > 0 && sh.Length > 0 && bin.FileAddress(sh) >= 0).ToList();
        for (int i = 0; i < maps.Count && EcuConnected; i++)
        {
            ProgressText = $"Sync: {i * 100 / maps.Count}%";
            SymbolHelper sh = maps[i];
            byte[] inFile = bin.ReadSymbol(sh);
            if (await Ecu.ReadMapAsync(sh) is not { } inEcu || inEcu.Length != inFile.Length || inEcu.AsSpan().SequenceEqual(inFile)) continue;
            if (toFile) bin.WriteData(bin.FileAddress(sh), inEcu);
            else await Ecu.WriteForcedAsync((int)sh.Start_address, inFile);
        }
        if (toFile)
        {
            bin.File.SetMemorySyncDate(ecu == DateTime.MinValue ? DateTime.Now : ecu);
            if (Settings.AutoChecksum) bin.UpdateChecksum();
            RefreshViewers(bin.FileName);
            await CheckChecksumAsync();
        }
        else await Ecu.WriteSyncDateAsync(file);
        ProgressText = "Synchronized";
    }

    protected override string ConnectFailedText => "Not connected";

    protected override bool OnlineMapsFromEcu => true;

    /// <summary>StartTableViewer for a map only in SRAM: from the ECU when connected, else from the loaded snapshot, else T5Suite's message.</summary>
    protected override Task OpenSramOnlyAsync(SuiteBinary bin, SymbolHelper sh)
    {
        if (EcuConnected) return OpenSramSymbolAsync(bin, sh);
        if (HasSramFile) OpenFromSramFile(sh);
        else ShowInfo("Symbol resides in SRAM and you are in offline mode. T5Suite is unable to fetch this symboldata in offline mode");
        return Task.CompletedTask;
    }

    protected override async Task DisconnectEcuAsync()
    {
        await KnockSnapshotAsync();
        await Ecu.DisconnectAsync();
    }

    protected override async Task<byte[]?> ReadEcuMapAsync(SymbolHelper sh) => await Ecu.ReadMapAsync(sh);

    protected override Task<bool> WriteEcuMapAsync(SymbolHelper sh, byte[] data) => Ecu.WriteMapAsync(sh, data);

    // ---- realtime ----

    public override RealtimeRules RealtimeRules => T5Realtime.Rules;


    // ---- flash over CAN ----

    /// <summary>Download flash from ECU: the T5 CAN Flasher's dump (its checksum check included).</summary>
    public override Task ReadEcuAsync(string file) => RunFlasherAsync(async () =>
    {
        bool ok = await Ecu.ReadFlashAsync(Settings, file);
        ShowInfo(ok ? $"Download done: {file}" : "Failed to download flash from ECU");
    });

    /// <summary>Upload flash to ECU: the open file, checked first as T7 / T8 flash theirs; conversions are asked by the library.</summary>
    public override async Task FlashEcuAsync(Func<string, Task<bool>> askYesNo)
    {
        if (await FlashableBinaryAsync(askYesNo) is not { } bin) return;
        await RunFlasherAsync(async () =>
        {
            WriteFlashResult result = await Ecu.WriteFlashAsync(Settings, bin.FileName);
            ShowInfo(result switch
            {
                WriteFlashResult.Done => "Flash sequence done",
                WriteFlashResult.Cancelled => "Flashing cancelled, the ECU was not touched",
                _ => "Failed to update flash. Don't switch the ECU off before you retry.",
            });
        });
    }

    // ---- SRAM snapshots ----

    /// <summary>Download SRAM in a project: &lt;project&gt;/Snapshots/Snapshot&lt;MMddyyyyHHmmss&gt;.RAM without asking; null without a project.</summary>
    public string? ProjectSnapshotFile()
    {
        if (Project is not { } project) return null;
        string dir = System.IO.Path.Combine(project.Dir, "Snapshots");
        System.IO.Directory.CreateDirectory(dir);
        return System.IO.Path.Combine(dir, $"Snapshot{DateTime.Now:MMddyyyyHHmmss}.RAM");
    }

    /// <summary>The name T5Suite proposed: Snapshot-&lt;bin&gt;-&lt;MMddyyyyHHmmss&gt;.RAM.</summary>
    public string SnapshotName => $"Snapshot-{System.IO.Path.GetFileNameWithoutExtension(Binary?.FileName ?? "")}-{DateTime.Now:MMddyyyyHHmmss}.RAM";

    /// <summary>Download SRAM from ECU: the 32 KB image.</summary>
    public async Task<bool> DownloadSramAsync(string file)
    {
        if (!EcuConnected)
        {
            ShowInfo("A canbus connection is needed to create a SRAM snapshot");
            return false;
        }
        ProgressText = "Downloading adaption data...";
        bool ok = await Ecu.SnapshotAsync(file);
        ProgressText = ok ? "Adaption data saved..." : "Could not read SRAM";
        return ok;
    }

    /// <summary>Compare ECU with binary: a snapshot next to the bin (T5Suite wrote it into the working directory), then the SRAM compare.</summary>
    public async Task CompareEcuWithBinaryAsync()
    {
        if (Binary is not { } bin || !EcuConnected) return;
        string file = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(bin.FileName) ?? "", SnapshotName);
        if (await DownloadSramAsync(file)) await CompareToSramAsync(file);
    }

    /// <summary>
    /// Upload SRAM to ECU: each map in flash and SRAM from the snapshot into the ECU (forced writes), and into the file too when asked
    /// (no transaction entries, as T5Suite); both dates are "now".
    /// </summary>
    public async Task UploadSramAsync(string file, bool toFileToo)
    {
        if (T5 is not { } bin || !EcuConnected) return;
        byte[] ram = await System.IO.File.ReadAllBytesAsync(file);
        ProgressText = "Restoring ECU state...";
        foreach (SymbolHelper sh in bin.Symbols.Cast<SymbolHelper>().Where(sh => sh.Start_address > 0 && sh.Length > 0 && bin.FileAddress(sh) >= 0).ToList())
        {
            byte[] data = SuiteCompare.ReadSram(ram, sh.Start_address, sh.Length);
            await Ecu.WriteForcedAsync((int)sh.Start_address, data);
            if (toFileToo) bin.WriteData(bin.FileAddress(sh), data);
        }
        await Ecu.WriteSyncDateAsync(DateTime.Now);
        if (toFileToo)
        {
            bin.File.SetMemorySyncDate(DateTime.Now);
            if (Settings.AutoChecksum) bin.UpdateChecksum();
            RefreshViewers(bin.FileName);
            await CheckChecksumAsync();
        }
        ProgressText = "Idle";
    }

    /// <summary>Clear knock counters: online only, no question and no message (T5Suite).</summary>
    [RelayCommand]
    private Task ClearKnockCounters() => EcuConnected && Binary is { } bin ? Ecu.ClearKnockCountersAsync(bin.Find) : Task.CompletedTask;

    // ---- error counters: T5's "DTC codes" ----

    /// <summary>Read DTC codes: the SRAM error counters that aren't 0, as "count N".</summary>
    public override async Task<List<FaultCode>?> ReadFaultCodesAsync()
    {
        if (Binary is not { } bin || !await EnsureConnectedAsync()) return null;
        var counters = await Ecu.ReadErrorCountersAsync(bin.Symbols.Cast<SymbolHelper>());
        return counters.Select(c => new FaultCode(c.Symbol.Varname, $"count {c.Count}" + (string.IsNullOrEmpty(c.Symbol.Description) ? "" : ": " + c.Symbol.Description))).ToList();
    }

    /// <summary>Clear: the counter set to 0 (T5Suite cleared every *_error and left *_fel), then the list again.</summary>
    public override async Task<List<FaultCode>?> ClearFaultCodeAsync(string code)
    {
        if (Binary?.Find(code) is { Start_address: > 0 } sh) await Ecu.WriteForcedAsync((int)sh.Start_address, new byte[sh.Length]);
        return await ReadFaultCodesAsync();
    }
}
