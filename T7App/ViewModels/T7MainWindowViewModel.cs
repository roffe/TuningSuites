using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommonSuite;
using CommunityToolkit.Mvvm.Input;
using SuiteApp.ViewModels;
using T7;
using TrionicCANLib.API;
using TrionicCANLib.Checksum;
using TrionicCANLib.Firmware;

namespace T7App.ViewModels;

/// <summary>
/// T7Suite's main window (frmMain): T7 binaries, and the features only T7Suite has or that aren't shared yet (ECU, realtime,
/// logs, SID, the tools).
/// </summary>
public partial class T7MainWindowViewModel : MainWindowViewModel
{
    public T7MainWindowViewModel() : base("T7SuitePro", "T7Suite", new T7SuiteRegistry())
    {
        Trionic7File.onProgress += (_, e) => Dispatcher.UIThread.Post(() => ProgressText = e.Percentage >= 55 ? "" : e.Info);
        InitEcu();
    }

    protected override uint FileLength => FileT7.Length;

    protected override bool IsValidFile(string path) => T7Binary.IsValidFile(path);

    protected override string? InvalidFileMessage => "File is not a Trionic 7 binary file!";

    protected override SuiteBinary OpenBinary(string path) => T7Binary.Open(path, Settings.ApplicationLanguage, Settings.AutoFixFooter);

    protected override string ReleaseTagPrefix => "T7suite_v";

    protected override string? LegacyMruKey => @"Software\T7SuitePro\MRUList";

    public override string PackageFilesName => "Trionic 7 packages";

    public override string PackageExtension => "t7p";

    // the footer of a file to compare with isn't fixed
    protected override SuiteBinary OpenCompareBinary(string path) => T7Binary.Open(path, Settings.ApplicationLanguage, false);

    public override PartInfo? LookupPartNumber(string partNumber) => BinaryTools.LookupPartNumber(partNumber);

    public override IReadOnlyList<MapShortcut> MyMapsDefaults { get; } =
        [new("Fuel", "Main fuel map", "BFuelCal.Map"), new("Boost", "Boost bias map", "BoostCal.RegMap"), new("Boost", "P factors map", "BoostCal.PMap")];

    /// <summary>My Maps' "targetafr" / "feedbackafr" entries were T7Suite's AFR maps, which need the realtime features.</summary>
    protected override bool OpenSpecialShortcut(MapShortcut shortcut)
    {
        if (shortcut.Symbol is not ("targetafr" or "feedbackafr")) return false;
        ShowInfo("The AFR maps come with the realtime features.");
        return true;
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(Binary)) OnPropertyChanged(nameof(OriginalFile));
    }

    /// <summary>The ribbon's Verify checksum: with AutoChecksum a mismatch offers to recalculate.</summary>
    protected override async Task VerifyChecksumAsync()
    {
        if (Binary is not { } bin) return;
        ChecksumResult result = await Task.Run(() => ChecksumT7.VerifyChecksum(bin.FileName, Settings.AutoChecksum, Settings.AutoFixFooter,
            (_, _, _) => UserPrompt.AskYesNo("Checksums did not verify ok, do you want to recalculate and update the checksums?", "Question")));
        ShowInfo(result == ChecksumResult.Ok ? "Checksums verified and all matched!" : "Checksums did not verify ok!");
    }

    /// <summary>Create project's prefill: the footer's car description, "&lt;partnumber&gt; &lt;software version&gt;".</summary>
    protected override (string carModel, string projectName) ProjectDefaults(SuiteBinary bin)
    {
        var header = new T7FileHeader();
        header.init(bin.FileName, false);
        return (header.getCarDescription().Trim(), header.getPartNumber().Trim() + " " + header.getSoftwareVersion().Trim());
    }

    /// <summary>After the settings dialog: also the footer fix for writes, the SRAM viewers' timer and the AFR captions.</summary>
    public override void SettingsChanged()
    {
        base.SettingsChanged();
        if (Binary is T7Binary bin) bin.AutoFixFooter = Settings.AutoFixFooter;
        OnPropertyChanged(nameof(FeedbackMapCaption));
        OnPropertyChanged(nameof(ClearFeedbackCaption));
    }

    /// <summary>Actions → Airmass result viewer, when the bin has the tables it needs (T7Suite silently did nothing otherwise).</summary>
    [RelayCommand]
    private void ShowAirmassResult()
    {
        if (Binary is not T7Binary bin) return;
        if (!T7AirmassResult.Available(bin))
        {
            ShowInfo("This file lacks the pedal, torque or airmass tables the airmass result viewer needs");
            return;
        }
        (int, double) compressor = (CompressorMap.T1752, 2.0);
        try
        {
            compressor = T7AirmassResult.CompressorDefaults(T7.FirmwareInfo.Read(bin).PartNumber);
        }
        catch (Exception)
        {
            // ponytail: an unreadable footer keeps the T1752 / 2.0 litre defaults
        }
        ShowDocument(new AirmassResultViewModel(this, bin.FileName, new AirmassSuite(
            (file, options) => new T7AirmassResult(T7Binary.Open(file, Settings.ApplicationLanguage, false), options),
            T7Binary.IsValidFile, "File is not a Trionic 7 binary file!", "Car is a convertible", false,
            ["Reverse", "First gear", "Second gear", "Third gear", "Fourth gear", "Fifth gear"],
            [
                (AirmassLimitType.TorqueLimiterEngine, "Engine torque limiter"), (AirmassLimitType.AirmassLimiter, "Airmass limiter"),
                (AirmassLimitType.TurboSpeedLimiter, "Turbospeed limiter"), (AirmassLimitType.TorqueLimiterEngineE85, "E85 engine torque limiter"),
                (AirmassLimitType.TorqueLimiterEngineE85Auto, "E85Auto engine torque limiter"), (AirmassLimitType.TorqueLimiterGear, "Gear torque limiter"),
                (AirmassLimitType.FuelCutLimiter, "Fuelcut limiter"), (AirmassLimitType.OverBoostLimiter, "Overboost limiter"),
            ],
            compressor)));
    }

    /// <summary>"Compare to original file": the stock bin with this part number, when Binaries has exactly one.</summary>
    public string? OriginalFile => Binary is T7Binary bin ? bin.OriginalFile() : null;

    [RelayCommand]
    private Task CompareToOriginal() => OriginalFile is { } file ? CompareToFileAsync(file) : Task.CompletedTask;

    // ---- firmware information ----

    public FirmwareInfoViewModel? FirmwareInfo() =>
        Binary is T7Binary bin && File.Exists(bin.FileName) ? new FirmwareInfoViewModel(T7.FirmwareInfo.Read(bin), Settings.WriteTimestampInBinary) : null;

    /// <summary>The firmware information dialog's OK: patches the current bin and updates its checksum.</summary>
    public void ApplyFirmware(FirmwareInfoViewModel firmware, Func<string, bool> askYesNo)
    {
        if (Binary is not T7Binary bin) return;
        try
        {
            int before = TransactionLog?.TransCollection.Count ?? 0;
            T7.FirmwareInfo.Apply(bin, firmware.ToEdit(), Settings.AutoFixFooter, Settings.WriteTimestampInBinary, askYesNo, TransactionLog);
            TransactionsAdded(before);
            RefreshViewers(bin.FileName);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShowInfo("Failed to write to binary. Is it read-only? Details: " + e.Message);
        }
    }
}
