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

public record FaultCode(string Code, string Description);

/// <summary>The ECU side of the main window (frmMain's CAN flasher, SRAM and fault code features).</summary>
public partial class T7MainWindowViewModel
{
    public T7Ecu Ecu { get; } = new();

    /// <summary>m_RealtimeConnectedToECU.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectCaption))]
    private bool _isConnected;

    /// <summary>The imported SRAM snapshot (.RAM), "SRAM: name" in the status bar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSramFile))]
    private string? _sramFile;

    partial void OnSramFileChanged(string? value) => SramFileText = value == null ? "" : "SRAM: " + Path.GetFileNameWithoutExtension(value);

    public bool HasSramFile => SramFile != null && File.Exists(SramFile);

    public override bool HasEcu => true;

    protected override bool EcuConnected => Ecu.IsConnected;

    public override string? CloseBlocker => Ecu.IsFlashing ? "Wait until the ECU operation has finished before closing T7Suite." : null;

    public override void Shutdown() => Ecu.Dispose();

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

    // ---- realtime ----

    /// <summary>The open realtime panel.</summary>
    public RealtimeViewModel? Realtime => Viewers.OfType<RealtimeViewModel>().FirstOrDefault();

    /// <summary>Toggle realtime panel [SHIFT+F1]: opens the panel and starts polling, or closes it.</summary>
    [RelayCommand]
    private async Task ToggleRealtimePanel()
    {
        if (Realtime is { } open)
        {
            await CloseViewerAsync(open);
            return;
        }
        if (Binary is not T7Binary bin) return;
        var panel = new RealtimeViewModel(this, bin);
        ShowDocument(panel);
        // not awaited: the command has to stay free to close the panel again
        _ = panel.StartAsync();
    }

    /// <summary>View knock count / false knock / real knock / misfire map (ShowRealtimeMapFromECU): read from SRAM.</summary>
    [RelayCommand]
    private async Task ShowEcuMap(string name)
    {
        if (Binary is not T7Binary bin) return;
        if (bin.FindAny(name) is not { } sh)
        {
            ShowInfo($"Symbol {name} does not exist in this file");
            return;
        }
        await OpenSramSymbolAsync(bin, sh);
    }

    /// <summary>Write log marker [F6].</summary>
    [RelayCommand]
    private void WriteLogMarker() => Realtime?.WriteLogMarkerCommand.Execute(null);

    /// <summary>Add to realtime list (symbol list): into the open panel, or into rtsymbols.txt for the next one.</summary>
    public void AddToRealtime(SymbolHelper sh)
    {
        RealtimeSymbol symbol = T7.Realtime.FromSymbol(sh);
        if (Realtime is { } panel)
        {
            panel.Add(symbol);
            return;
        }
        string file = Path.Combine(SettingsKey.Folder(Suite), "rtsymbols.txt");
        var rows = T7.Realtime.LoadLayout(file, Binary as T7Binary).Where(r => r.Name != symbol.Name).Append(symbol).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        T7.Realtime.SaveLayout(file, rows);
    }

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

    // ---- SRAM maps ----

    /// <summary>A map that only lives in SRAM: read it from the ECU (connecting first) and show it.</summary>
    protected override async Task OpenSramSymbolAsync(SuiteBinary bin, SymbolHelper sh)
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
    public override async Task ReadMapFromEcuAsync(MapViewerViewModel viewer)
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
    public override async Task WriteMapToEcuAsync(MapViewerViewModel viewer)
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
        if (Binary is not T7Binary bin || !File.Exists(bin.FileName))
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
    public void OpenFromSramFile(SymbolHelper sh, string? file = null)
    {
        file ??= SramFile;
        if (Binary is not T7Binary bin || file == null || !File.Exists(file)) return;
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

    // ---- SRAM compares ----

    /// <summary>An SRAM snapshot, laid out by the open bin's symbols: "SRAM Symbol" viewers.</summary>
    private CompareSide SramSide(T7Binary bin, string file)
    {
        byte[] ram = File.ReadAllBytes(file);
        return new(file, name => bin.FindAny(name) is { } sh ? SuiteCompare.ReadSram(ram, sh.Start_address, sh.Length) : null,
            name => { if (bin.FindAny(name) is { } sh) OpenFromSramFile(sh, file); });
    }

    /// <summary>Compare to SRAM snapshot: "SRAM &lt;&gt; BIN Compare results", the bin's map and the snapshot's per symbol.</summary>
    public async Task CompareToSramAsync(string file)
    {
        if (Binary is not T7Binary bin) return;
        List<CompareRow> rows = await Task.Run(() => SuiteCompare.CompareToSram(bin, File.ReadAllBytes(file)));
        string name = Path.GetFileName(file);
        ShowDocument(new CompareResultsViewModel(this, bin, CompareSide.Bin(this, bin), SramSide(bin, file), rows,
            $"SRAM <> BIN Compare results: {name}", $"SRAM symbol difference: {{0}} [{name}]"));
    }

    /// <summary>Compare SRAM snapshots: both snapshots' maps per symbol.</summary>
    public async Task CompareSramAsync(string file1, string file2)
    {
        if (Binary is not T7Binary bin) return;
        List<CompareRow> rows = await Task.Run(() =>
            SuiteCompare.CompareSram(bin, File.ReadAllBytes(file1), File.ReadAllBytes(file2)));
        string a = Path.GetFileName(file1), b = Path.GetFileName(file2);
        ShowDocument(new CompareResultsViewModel(this, bin, SramSide(bin, file1), SramSide(bin, file2), rows,
            $"SRAM compare results: {a} {b}", $"SRAM symbol difference: {{0}} [{a} vs {b}]"));
    }

    private static IEnumerable<SymbolHelper> SyncSymbols(T7Binary bin) =>
        bin.Symbols.Cast<SymbolHelper>().Where(sh => sh.Start_address > 0x80000 && bin.IsCalibration(sh.SmartVarname)
            && bin.AddressOf(sh) is > 0 and < 0x80000).ToList();
}
