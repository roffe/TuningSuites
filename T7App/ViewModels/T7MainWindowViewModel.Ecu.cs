using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommonSuite;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MapControls;
using SuiteApp.ViewModels;
using T7;
using TrionicCANLib.Checksum;

namespace T7App.ViewModels;

/// <summary>The ECU side of the main window over T7Ecu (frmMain's CAN flasher, SRAM and fault code features).</summary>
public partial class T7MainWindowViewModel
{
    public T7Ecu Ecu { get; } = new();

    protected override bool EcuConnected => Ecu.IsConnected;

    public override string? CloseBlocker => Ecu.IsFlashing ? "Wait until the ECU operation has finished before closing T7Suite." : null;

    public override void Shutdown() => Ecu.Dispose();

    private void InitEcu()
    {
        Ecu.Info += (_, e) => Dispatcher.UIThread.Post(() => ProgressText = e.Info);
        Ecu.Progress += (_, p) => Dispatcher.UIThread.Post(() => ProgressText = p >= 100 ? "Done" : $"{p} %");
    }

    protected override async Task<string?> ConnectEcuAsync() => await Ecu.ConnectAsync(Settings) ? "Connected" : null;

    protected override string ConnectFailedText => "Failed to start KWP session";

    protected override Task DisconnectEcuAsync() => Ecu.DisconnectAsync();

    // the library never reports a failed read, it returns zeros
    protected override async Task<byte[]?> ReadEcuMapAsync(SymbolHelper sh) => await Ecu.ReadMapAsync(sh);

    protected override Task<bool> WriteEcuMapAsync(SymbolHelper sh, byte[] data) => Ecu.WriteMapAsync(sh, data);

    protected override string WriteRefusedText(string map) => base.WriteRefusedText(map) + " Writing to SRAM needs an open binary in the ECU.";

    // ---- realtime ----

    public override RealtimeRules RealtimeRules => T7Realtime.Rules;

    protected override RealtimeViewModel CreateRealtimePanel(SuiteBinary bin) => new T7RealtimeViewModel(this, (T7Binary)bin);

    // ---- AFR maps ----

    private AfrFeedback? m_afr;

    /// <summary>The open bin's AFR target / feedback / counter maps (AFRMaps folder next to it).</summary>
    public AfrFeedback? AfrMaps => Binary is not T7Binary bin ? null : m_afr?.Binary == bin ? m_afr : m_afr = new AfrFeedback(bin);

    /// <summary>Autotune without auto update: the cells (data indices) to take from the proposed percentages, null to cancel.</summary>
    public Func<double[], Task<IReadOnlyCollection<int>?>>? AcceptAutotune { get; set; }

    /// <summary>SetupMeasureAFRorLambda's captions.</summary>
    public string FeedbackMapCaption => Settings.MeasureAFRInLambda ? "Show lambda feedback map" : "Show AFR feedback map";
    public string ClearFeedbackCaption => Settings.MeasureAFRInLambda ? "Clear lambda feedback map" : "Clear AFR feedback map";

    [RelayCommand]
    private void ShowAfrTargetMap() => ShowAfr("TargetAFR");

    [RelayCommand]
    private void ShowAfrFeedbackMap() => ShowAfr("FeedbackAFR");

    [RelayCommand]
    private void ShowAfrCounterMap() => ShowAfr("FeedbackCounter");

    // SaveMap first, as T7Suite did before showing an AFR viewer
    private void ShowAfr(string kind)
    {
        if (AfrMaps is not { } afr) return;
        afr.Save();
        if (CreateAfrViewer(afr, kind) is { } viewer) ShowDocument(viewer);
    }

    /// <summary>
    /// ShowAfrMAP: TargetAFR / FeedbackAFR (×0.1) or FeedbackCounter on BFuelCal.Map's axes, 16-bit, upside down. The target
    /// map saves into its .afr file, the other two are read-only.
    /// </summary>
    private MapViewerViewModel? CreateAfrViewer(AfrFeedback afr, string kind)
    {
        T7Binary bin = afr.Binary;
        if (bin.FindAny("BFuelCal.Map") is not { } fuel) return null;
        byte[] content = kind switch { "TargetAFR" => afr.Target, "FeedbackAFR" => afr.Feedback, _ => afr.Counter };
        var (_, _, xDescr, yDescr, _) = bin.AxisSymbols("BFuelCal.Map");
        var map = new MapData(kind, content, AfrFeedback.Columns, true)
        {
            Factor = kind == "FeedbackCounter" ? 1 : 0.1,
            UpsideDown = true,
            XAxis = bin.GetXaxisValues("BFuelCal.Map").Select(v => (double)v).ToArray(),
            YAxis = bin.GetYaxisValues("BFuelCal.Map").Select(v => (double)v).ToArray(),
            XName = xDescr,
            YName = yDescr,
            ZName = kind == "FeedbackCounter" ? "count" : "AFR",
        };
        if (bin.OpenLoopTable("BFuelCal.Map") is { Length: > 0 } ol) map.OpenLoop = MapData.Decode(ol, true).Select(v => (double)v).ToArray();
        AppSettings settings = Settings;
        bool target = kind == "TargetAFR";
        return new MapViewerViewModel
        {
            Owner = this,
            Binary = bin,
            Symbol = fuel,
            MapName = kind,
            Map = map,
            Address = -1,
            IsReadOnly = !target,
            IsAfrMap = true,
            SaveTo = target ? afr.SaveTarget : null,
            ReadFrom = target ? () => afr.Target : null,
            ViewType = MapViewType.Easy,
            IsRedWhite = settings.ShowRedWhite,
            DisableColors = settings.DisableMapviewerColors,
            GraphVisible = settings.ShowGraphs,
            OpenLoopMark = (OpenLoopMark)Math.Clamp(settings.StandardFill, 0, 2),
        };
    }

    /// <summary>Clear AFR feedback map: feedback and counters to 0, saved, open viewers refreshed.</summary>
    [RelayCommand]
    private void ClearAfrFeedbackMap()
    {
        if (!Settings.AutoCreateAFRMaps || AfrMaps is not { } afr) return;
        afr.Clear();
        RefreshAfrViewers();
    }

    /// <summary>UpdateFeedbackMaps: open feedback and counter viewers show the maps as they are now.</summary>
    public void RefreshAfrViewers()
    {
        if (m_afr == null) return;
        foreach (MapViewerViewModel v in Viewers.OfType<MapViewerViewModel>().Where(v => v.IsAfrMap && v.MapName != "TargetAFR" && v.FileName == m_afr.Binary.FileName))
            v.Map.Load(v.MapName == "FeedbackAFR" ? m_afr.Feedback : m_afr.Counter);
    }

    /// <summary>Import AFR feedback data: the measured cells corrected into BFuelCal.Map, the checksum, the feedback cleared.</summary>
    [RelayCommand]
    private void ImportAfrFeedback()
    {
        if (Binary is not T7Binary bin || AfrMaps is not { } afr || bin.FindAny("BFuelCal.Map") is not { } fuel || bin.ReadSymbol(fuel) is not { } map) return;
        byte[] data = AfrFeedback.ApplyFeedback(map, afr.Target, afr.Feedback, afr.Counter, Settings.MeasureAFRInLambda);
        int before = TransactionLog?.TransCollection.Count ?? 0;
        try
        {
            bin.WriteSymbol(bin.FileAddress(fuel), data, Settings.AutoFixFooter, TransactionLog, "Imported AFR feedback data");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShowInfo(e.Message);
            return;
        }
        TransactionsAdded(before);
        afr.Clear();
        RefreshAfrViewers();
        RefreshViewers(bin.FileName);
    }

    // ---- flashing ----

    /// <summary>Read ECU: the flash into a file; the result is the library's own message.</summary>
    public override Task ReadEcuAsync(string file) => RunFlasherAsync(async () =>
    {
        var (ok, message) = await Ecu.ReadFlashAsync(Settings, file);
        ShowInfo(ok ? $"Download done: {file}" : message);
    });

    /// <summary>Flash current file to ECU, the file on disk once it's ready (FlashableBinaryAsync).</summary>
    public override async Task FlashEcuAsync(Func<string, Task<bool>> askYesNo)
    {
        if (await FlashableBinaryAsync(askYesNo) is not { } bin) return;
        await RunFlasherAsync(async () =>
        {
            var (ok, message) = await Ecu.WriteFlashAsync(Settings, bin.FileName);
            ShowInfo(ok ? "Flash sequence done" : message);
        });
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

    /// <summary>Get fault codes (OBDII): obdFaults from SRAM, with descriptions. Codes without one are listed too (T7Suite hid them).</summary>
    public override async Task<List<FaultCode>?> ReadFaultCodesAsync()
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
        return Describe(await Ecu.ReadFaultCodesAsync(obdFaults));
    }

    public override async Task<List<FaultCode>?> ClearFaultCodeAsync(string code)
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

    // ---- synchronize ----

    /// <summary>Synchronize to binary: every calibration symbol in SRAM into the file, then the checksum.</summary>
    public async Task SyncToBinaryAsync()
    {
        if (Binary is not T7Binary bin) return;
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
        if (Binary is not T7Binary bin) return;
        if (!await EnsureConnectedAsync())
        {
            ShowInfo("An active CAN bus connection is needed to get data from the ECU");
            return;
        }
        var refused = new List<string>();
        foreach (SymbolHelper sh in SyncSymbols(bin))
        {
            ProgressText = "Sync to ECU: " + sh.SmartVarname;
            if (!await Ecu.WriteMapAsync(sh, bin.Read((int)bin.AddressOf(sh), sh.Length))) refused.Add(sh.SmartVarname);
        }
        ProgressText = "";
        ReportRefused(refused);
    }

    private void ReportRefused(List<string> refused)
    {
        if (refused.Count > 0)
            ShowInfo($"The ECU did not accept {refused.Count} map(s), starting with {refused[0]}. Writing to SRAM needs an open binary in the ECU.");
    }

    // ---- tuning packages ----

    /// <summary>Upload tuning package to ECU: each map of a .t7p that the bin has into SRAM, not the checksum switch.</summary>
    public async Task UploadPackageAsync(string file)
    {
        if (Binary is not T7Binary bin) return;
        if (!await EnsureConnectedAsync())
        {
            ShowInfo("An active CAN bus connection is needed to upload a tuning package");
            return;
        }
        var refused = new List<string>();
        foreach (var (name, data) in SymbolFiles.ReadPackage(file))
        {
            if (name == "MapChkCal.ST_Enable" || bin.FindAny(name) is not { } sh || bin.AddressOf(sh) <= 0) continue;
            ProgressText = "Uploading: " + name;
            if (!await Ecu.WriteMapAsync(sh, data)) refused.Add(name);
        }
        ProgressText = "";
        ReportRefused(refused);
    }

    /// <summary>Generate tuning package from ECU: the fixed package's maps read from SRAM into a .t7p.</summary>
    public async Task GeneratePackageAsync(string file)
    {
        if (Binary is not T7Binary bin) return;
        if (!await EnsureConnectedAsync())
        {
            ShowInfo("An active CAN bus connection is needed to download a tuning package");
            return;
        }
        if (File.Exists(file)) File.Delete(file);
        var exporter = new PackageExporter();
        foreach (SymbolHelper sh in SymbolFiles.FixedPackage(bin))
        {
            ProgressText = "Downloading: " + sh.Varname;
            exporter.ExportMap(file, sh.Varname, sh.Userdescription, sh.Length, await Ecu.ReadMapAsync(sh));
        }
        ProgressText = "";
    }

    private static IEnumerable<SymbolHelper> SyncSymbols(T7Binary bin) =>
        bin.Symbols.Cast<SymbolHelper>().Where(sh => sh.Start_address > 0x80000 && bin.IsCalibration(sh.SmartVarname)
            && bin.AddressOf(sh) is > 0 and < 0x80000).ToList();
}
