using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommonSuite;
using CommunityToolkit.Mvvm.Input;
using SuiteApp.ViewModels;
using T8SuitePro;
using TrionicCANLib.Checksum;

namespace T8App.ViewModels;

/// <summary>The ECU side over T8Ecu: T8Suite's Realtime page (connect, ECU information, fault codes) and its CAN Flasher group.</summary>
public partial class T8MainWindowViewModel
{
    public T8Ecu Ecu { get; } = new();

    protected override bool EcuConnected => Ecu.IsConnected;

    public override string? CloseBlocker => Ecu.IsFlashing ? "Wait until the ECU operation has finished before closing T8Suite." : null;

    public override void Shutdown() => Ecu.Dispose();

    private void InitEcu()
    {
        Ecu.Info += (_, e) => Dispatcher.UIThread.Post(() => ProgressText = e.Info);
        Ecu.Progress += (_, p) => Dispatcher.UIThread.Post(() => ProgressText = p >= 100 ? "Done" : $"{p} %");
    }

    // "Connected", with the ECU's software version that T8Suite showed in a status bar item of its own
    protected override async Task<string?> ConnectEcuAsync() =>
        await Ecu.ConnectAsync(Settings) is { } sw ? (sw.Trim() == "" ? "Connected" : $"Connected: {sw.Trim()}") : null;

    protected override string ConnectFailedText => "Failed to connect";

    protected override Task DisconnectEcuAsync() => Ecu.DisconnectAsync();

    protected override async Task<byte[]?> ReadEcuMapAsync(SymbolHelper sh) => await Ecu.ReadMapAsync(sh);

    protected override Task<bool> WriteEcuMapAsync(SymbolHelper sh, byte[] data) => Ecu.WriteMapAsync(sh, data);

    // ---- realtime ----

    public override RealtimeRules RealtimeRules => T8Realtime.Rules;

    /// <summary>The shared panel over T8Suite's GMLAN passes (dynamic list unless "Prefer dynamic retrieval of live data" is off).</summary>
    protected override RealtimeViewModel CreateRealtimePanel(SuiteBinary bin) =>
        new(this, bin, T8Realtime.Rules, new T8RealtimeEngine(Ecu, Settings.PreferDynamicLiveData));

    /// <summary>MapViewerEx's timer only read the maps that live in SRAM alone.</summary>
    protected override bool AutoUpdates(MapViewerViewModel viewer) => base.AutoUpdates(viewer) && viewer.Symbol.Flash_start_address >= 0x100000;

    // ---- the CAN Flasher group ----

    /// <summary>Read ECU: "Download done" or the library's last message (T8Suite said nothing when it failed).</summary>
    public override Task ReadEcuAsync(string file) => RunFlasherAsync(async () =>
    {
        var (ok, message) = await Ecu.ReadFlashAsync(Settings, file);
        ShowInfo(ok ? $"Download done: {file}" : message);
    });

    /// <summary>
    /// Flash current file to ECU: "Flash sequence done", "Failed to update flash", or, when the flash was erased but not
    /// programmed, the offer to recover with the same file (T8Suite compared the answer with OK, so recovery never started).
    /// </summary>
    public override async Task FlashEcuAsync(Func<string, Task<bool>> askYesNo)
    {
        if (await FlashableBinaryAsync(askYesNo) is not { } bin) return;
        bool recover = false;
        await RunFlasherAsync(async () =>
        {
            var (ok, needRecovery, message) = await Ecu.WriteFlashAsync(Settings, bin.FileName);
            if (ok) ShowInfo("Flash sequence done");
            else if (needRecovery) recover = await askYesNo("Flash was erased but programming failed, do you which to attempt to recover the ECU?");
            else ShowInfo("Failed to update flash" + (message == "" ? "" : ": " + message));
        });
        if (recover) await RecoverEcuAsync(bin.FileName);
    }

    /// <summary>Recover ECU with a T8 bin whose checksum verifies, as TrionicCANFlasher checks (T8Suite took any file).</summary>
    public async Task RecoverEcuAsync(string file)
    {
        if (!await Task.Run(() => T8Binary.IsValidFile(file))) return;
        if (await VerifyFileChecksumAsync(file) is var checksum and not ChecksumResult.Ok)
        {
            ShowInfo("Checksum check failed: " + checksum);
            return;
        }
        await RunFlasherAsync(async () =>
        {
            var (ok, message) = await Ecu.RecoverAsync(Settings, file);
            ShowInfo(ok ? "Recovery done" : "Failed to recover ECU" + (message == "" ? "" : ": " + message));
        });
    }

    // ---- the Realtime page ----

    /// <summary>Get ECU information: frmECUInformation's fields; T8Suite showed nothing when the ECU didn't answer.</summary>
    public async Task<List<EcuInfoRow>?> ReadEcuInfoAsync()
    {
        IsBusy = true;
        try
        {
            List<EcuInfoRow>? rows = await Ecu.ReadInfoAsync(Settings);
            if (rows == null) ShowInfo("Unable to connect to Trionic 8 ECU");
            return rows;
        }
        catch (InvalidOperationException e)
        {
            ShowInfo(e.Message);
            return null;
        }
        finally
        {
            IsBusy = false;
            ProgressText = "";
        }
    }

    /// <summary>Get fault codes (OBDII): the realtime connection closes, as in T8Suite, and ReadDTC runs on a session of its own.</summary>
    public override async Task<List<FaultCode>?> ReadFaultCodesAsync()
    {
        await DisconnectRealtimeAsync();
        try
        {
            if (await Ecu.ReadFaultCodesAsync(Settings) is { } codes) return Describe(codes);
            ShowInfo("Unable to connect to Trionic 8 ECU");
        }
        catch (InvalidOperationException e)
        {
            ShowInfo(e.Message);
        }
        return null;
    }

    /// <summary>The window's Clear on a "P" code: every code (the library clears them all), then the codes again.</summary>
    public override async Task<List<FaultCode>?> ClearFaultCodeAsync(string code)
    {
        if (!code.StartsWith('P')) return null;
        try
        {
            if (!await Ecu.ClearFaultCodesAsync(Settings)) ShowInfo("Clear DTC codes was failed");
        }
        catch (InvalidOperationException e)
        {
            ShowInfo(e.Message);
            return null;
        }
        return await ReadFaultCodesAsync();
    }

    /// <summary>Clear DTC and knock counters, with T8Suite's answer (T7Suite gave none).</summary>
    [RelayCommand]
    private async Task ClearDtcAndKnockCounters()
    {
        await DisconnectRealtimeAsync();
        try
        {
            ShowInfo(await Ecu.ClearDtcAndKnockCountersAsync(Settings) ? "Clear DTC codes was successful" : "Clear DTC codes was failed");
        }
        catch (InvalidOperationException e)
        {
            ShowInfo(e.Message);
        }
    }
}
