using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommonSuite;
using SuiteApp.ViewModels;
using Trionic5Tools;
using TrionicCANLib.Checksum;

namespace T5App.ViewModels;

/// <summary>T5Suite 2.0's main window (frmMain): T5 binaries on the shared window; T5Suite's own features come chunk by chunk.</summary>
public partial class T5MainWindowViewModel : MainWindowViewModel
{
    public T5MainWindowViewModel() : base(T5AppSettings.Suite, "T5Suite", new T5SuiteRegistry())
    {
        T5Settings = new T5AppSettings();
        MigrateCanDevice();
        InitEcu();
    }

    /// <summary>
    /// Once, T5Suite's own names as the shared settings: CanDevice ("Lawicel", "CombiAdapter" / "Multiadapter", "Just4Trionic", "Kvaser")
    /// as the adapter type, "Use wideband lambda through symbol" (AD_EGR / AD_cat; AD_sond stays the narrowband) as the wideband symbol.
    /// </summary>
    private void MigrateCanDevice()
    {
        bool hasAdapter, hasWideband;
        using (var key = SettingsKey.Open(T5AppSettings.Suite))
        {
            hasAdapter = key.GetValue("AdapterType") != null;
            hasWideband = key.GetValue("WideBandSymbol") != null;
        }
        if (!hasWideband)
        {
            Settings.WideBandSymbol = T5Settings.WidebandLambdaSymbol is "AD_cat" ? "AD_cat" : "AD_EGR";
            Settings.UseWidebandLambda = T5Settings.UseWidebandLambdaThroughSymbol && T5Settings.WidebandLambdaSymbol is "AD_EGR" or "AD_cat";
        }
        if (hasAdapter) return;
        string? adapter = T5Settings.CanDevice switch
        {
            "Lawicel" => "Lawicel CANUSB",
            "CombiAdapter" or "Multiadapter" => "CombiAdapter",
            "Just4Trionic" => "Just4Trionic",
            "Kvaser" => "Kvaser HS",
            _ => null, // the DIY adapter (mct_can.dll) has no driver here: the shared default stays
        };
        if (adapter != null) Settings.AdapterType = adapter;
    }

    /// <summary>
    /// T5Suite's own settings (realtime, autotune, notifications, ...), in the same settings.json as the shared ones. Read only the
    /// T5-only names through it: each object caches its values, the shared Settings own the names both have.
    /// </summary>
    public T5AppSettings T5Settings { get; }

    // T5Suite's title was "T5Suite Professional 2.0 [file]"; the settings keep T5Suite 2.0's name
    protected override string TitleName => "T5Suite";

    // the larger of the two; S19 files go through T5Suite's own converter
    protected override uint FileLength => 0x40000;

    protected override string? ConvertS19(string path) => new SrecordT5().ConvertSrecToBin(path, out string bin) ? bin : null;

    protected override bool IsValidFile(string path) => T5Binary.IsValidFile(path);

    protected override string? InvalidFileMessage => "File is not a Trionic 5 binary file!";

    protected override SuiteBinary OpenBinary(string path)
    {
        T5Binary bin = T5Binary.Open(path);
        bin.AutoDetectMapSensor = T5Settings.AutoDetectMapsensorType;
        return bin;
    }

    protected override string ReleaseTagPrefix => "T5suite_v";

    public override string PackageFilesName => "Trionic 5 packages";

    public override string PackageExtension => "t5p";

    /// <summary>Lookup partnumber: T5's own part number list.</summary>
    public override PartInfo? LookupPartNumber(string partNumber)
    {
        ECUInformation ecu = new PartNumberConverter().GetECUInfo(partNumber, "");
        if (!ecu.Valid) return null;
        // frmPartnumberLookup: Aero implies FPT, FPT implies turbo
        bool fpt = ecu.Isfpt || ecu.Isaero;
        return new PartInfo(partNumber, ecu.Carmodel.ToString().Replace('_', ' '), ecu.Enginetype.ToString().Replace('_', ' '), ecu.Bhp, ecu.Torque,
            !ecu.Is2point3liter, ecu.Is2point3liter, ecu.Isturbo || fpt, fpt, PartInfo.StockBinary(partNumber))
        {
            Extra =
            [
                new("Base boost", ecu.Baseboost + " bar"), new("Max. boost (M)", ecu.Max_stock_boost_manual + " bar"),
                new("Max. boost (AUT)", ecu.Max_stock_boost_automatic + " bar"), new("Stage I boost", ecu.Stage1boost + " bar"),
                new("Stage II boost", ecu.Stage2boost + " bar"), new("Stage III boost", ecu.Stage3boost + " bar"),
                new("MYs", ecu.MakeYearFrom == ecu.MakeYearUpto ? ecu.MakeYearFrom.ToString() : $"{ecu.MakeYearFrom}-{ecu.MakeYearUpto}"),
                new("Region", ecu.Region), new("ECU", ecu.Ecutype == "T5.2" ? "T5.2" : "T5.5"),
                new("Aero", ecu.Isaero ? "yes" : "no"), new("High altitude file", ecu.HighAltitude ? "yes" : "no"),
            ],
        };
    }

    // frmDefineMyMaps.CreateDefaultFile
    public override IReadOnlyList<MapShortcut> MyMapsDefaults { get; } =
    [
        new("Idle", "Idle RPM", "Idle_rpm_tab!"), new("Boost", "Boost Map", "Tryck_mat!"), new("Boost", "Reg Kon Mat", "Reg_kon_mat!"),
    ];

    /// <summary>On open the checksum is checked, and the status bar shows the ECU type, CPU speed and RAM lock (frmMain 929).</summary>
    protected override async Task OnOpenedAsync(SuiteBinary bin)
    {
        // "Always create AFR maps": the AFRMaps folder next to the bin
        if (T5Settings.AlwaysCreateAFRMaps) _ = AfrMaps;
        ShowFirmwareStatus();
        await CheckChecksumAsync();
    }

    private void ShowFirmwareStatus()
    {
        if (Binary is not T5Binary bin) return;
        Trionic5Properties p = bin.File.GetTrionicProperties();
        OpenClosedText = $"{(p.IsTrionic55 ? "T5.5" : "T5.2")} | {p.CPUspeed} | {(p.RAMlocked ? "RAM locked" : "RAM unlocked")}";
    }

    /// <summary>Actions → Trionic options (firmware): a copy of the file's properties for the options window.</summary>
    public FirmwareOptionsViewModel? FirmwareOptions()
    {
        if (Binary is not T5Binary bin) return null;
        Trionic5Properties p = bin.File.GetTrionicProperties();
        return new FirmwareOptionsViewModel(p)
        {
            HasRpmLimit = bin.File.GetHardcodedRPMLimitTwo(bin.FileName) > 0,
            PartnumberLength = Math.Max(p.Partnumber.Length, 1),
            VssCodeLength = Math.Max(p.VSSCode.Length, 1),
        };
    }

    // T5Suite's grid grouped by XDF category, then subcategory
    protected override string[] SymbolGroupPaths => [nameof(SymbolHelper.Category), nameof(SymbolHelper.Subcategory)];

    /// <summary>The firmware options dialog (the window sets it); true for Ok.</summary>
    public Func<FirmwareOptionsViewModel, Task<bool>>? ShowFirmwareOptions { get; set; }

    /// <summary>
    /// Pgm_mod! opens the firmware options instead of a map (StartTableViewer). T5Suite showed a read-only view of the file's or the
    /// ECU's bits there; here they can be changed, as from Actions → Trionic options.
    /// </summary>
    protected override bool OpenOther(SuiteBinary bin, SymbolHelper sh)
    {
        if (sh.SmartVarname != "Pgm_mod!" || ShowFirmwareOptions == null) return false;
        _ = EditFirmwareAsync();
        return true;
    }

    public async Task EditFirmwareAsync()
    {
        if (FirmwareOptions() is { } options && ShowFirmwareOptions != null && await ShowFirmwareOptions(options)) await ApplyFirmwareAsync(options.Properties);
    }

    /// <summary>The options window's Ok: the changed fields written (transactions in a project), the checksum updated, the status bar refreshed.</summary>
    public async Task ApplyFirmwareAsync(Trionic5Properties props)
    {
        if (Binary is not T5Binary bin) return;
        int before = TransactionLog?.TransCollection.Count ?? 0;
        try
        {
            bin.File.SetTransactionLog(TransactionLog);
            bin.File.SetAutoUpdateChecksum(Settings.AutoChecksum);
            bin.File.SetTrionicOptions(props);
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            ShowInfo("Failed to write to binary. Is it read-only? Details: " + e.Message);
        }
        finally
        {
            bin.File.SetTransactionLog(null);
        }
        TransactionsAdded(before);
        ShowFirmwareStatus();
        await CheckChecksumAsync();
    }

    protected override async Task VerifyChecksumAsync() => await CheckChecksumAsync();

    private async Task CheckChecksumAsync()
    {
        if (Binary is not { } bin) return;
        ChecksumResult result = await Task.Run(bin.VerifyChecksum);
        ChecksumText = result == ChecksumResult.Ok ? "Checksum: OK" : "Checksum: invalid";
    }

    /// <summary>Create project: the partnumber and software id from the firmware properties.</summary>
    protected override (string carModel, string projectName) ProjectDefaults(SuiteBinary bin)
    {
        Trionic5Properties p = ((T5Binary)bin).File.GetTrionicProperties();
        return (p.Carmodel, p.Partnumber.Trim() + " " + p.SoftwareID.Trim());
    }

    /// <summary>frmMain's gridSymbols: the description first, then the symbol; category, subcategory, addresses and length hidden.</summary>
    public override IReadOnlyList<SymbolColumn> SymbolColumns { get; } =
    [
        new("Description", nameof(SymbolHelper.Description)), new("Symbol", nameof(SymbolHelper.Varname)),
        new("Flash address", nameof(SymbolHelper.Flash_start_address), false), new("Length", nameof(SymbolHelper.Length), false),
        new("SRAM address", nameof(SymbolHelper.Start_address), false), new("Category", nameof(SymbolHelper.Category), false),
        new("Subcategory", nameof(SymbolHelper.Subcategory), false),
    ];

    /// <summary>Grouped by category (descending, as T5Suite sorted it), inside a category by subcategory, then description.</summary>
    protected override IEnumerable<SymbolHelper> OrderSymbols(IEnumerable<SymbolHelper> symbols) =>
        symbols.OrderByDescending(s => s.XdfCategory).ThenBy(s => s.XdfSubcategory).ThenBy(s => s.Description, StringComparer.Ordinal);

    public override bool ColorSymbolNames => false;

    public override bool ColorDescriptionsByCategory => true;
}
