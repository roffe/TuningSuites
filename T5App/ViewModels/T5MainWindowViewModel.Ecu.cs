using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommonSuite;
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
        return "Connected: " + sw;
    }

    protected override string ConnectFailedText => "Not connected";

    protected override Task DisconnectEcuAsync() => Ecu.DisconnectAsync();

    protected override async Task<byte[]?> ReadEcuMapAsync(SymbolHelper sh) => await Ecu.ReadMapAsync(sh);

    protected override Task<bool> WriteEcuMapAsync(SymbolHelper sh, byte[] data) => Ecu.WriteMapAsync(sh, data);

    // ---- realtime ----

    public override RealtimeRules RealtimeRules => T5Realtime.Rules;

    protected override RealtimeViewModel CreateRealtimePanel(SuiteBinary bin) => new(this, bin, T5Realtime.Rules, new T5RealtimeEngine(Ecu));

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
