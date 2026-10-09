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
/// logs, compare, tuning packages, SID, the tools).
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
        RestartSramTimer();
        OnPropertyChanged(nameof(FeedbackMapCaption));
        OnPropertyChanged(nameof(ClearFeedbackCaption));
    }

    /// <summary>
    /// File → Import tuning package: the .t7p's symbols and search &amp; replace patterns into the bin (transaction entries in a
    /// project), one checksum update; open viewers show the new data. The results for the "Import results" list.
    /// </summary>
    public List<PackageResult>? ImportTuningPackage(string file)
    {
        if (Binary is not T7Binary bin) return null;
        int before = TransactionLog?.TransCollection.Count ?? 0;
        try
        {
            List<PackageResult> results = TuningPackage.Read(file, bin).Apply(bin, Settings.AutoFixFooter, TransactionLog);
            TransactionsAdded(before);
            RefreshViewers(bin.FileName);
            return results;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShowInfo("Failed to import the tuning package: " + e.Message);
            return null;
        }
    }

    /// <summary>File → Edit a tuning package (one at a time, as T7Suite).</summary>
    [RelayCommand]
    private void EditTuningPackage()
    {
        if (Binary is not T7Binary bin) return;
        if (Viewers.OfType<TuningPackageEditorViewModel>().FirstOrDefault() is { } open)
        {
            SelectedViewer = open;
            return;
        }
        ShowDocument(new TuningPackageEditorViewModel(this, bin));
    }

    /// <summary>Information → Browse axis information (the symbol list's "Browse axis info" passes one symbol).</summary>
    [RelayCommand]
    public void BrowseAxes(string? symbol)
    {
        if (Binary is T7Binary bin) ShowDocument(new AxisBrowserViewModel(this, bin, symbol));
    }

    /// <summary>
    /// Show disassembly (full: the linear sweep): &lt;bin&gt;.asm / &lt;bin&gt;_full.asm next to the bin, redone when asked or when
    /// it isn't there yet, then shown beside the bin's bytes.
    /// </summary>
    public async Task ShowDisassemblyAsync(bool full, Func<string, Task<bool>> redo)
    {
        if (Binary is not T7Binary bin) return;
        string file = Path.Combine(Path.GetDirectoryName(bin.FileName) ?? "", Path.GetFileNameWithoutExtension(bin.FileName) + (full ? "_full.asm" : ".asm"));
        if (!File.Exists(file) || await redo("Assemblerfile already exists, do you want to redo the disassembly?"))
        {
            IsBusy = true;
            ProgressText = "Disassembling...";
            try
            {
                await Task.Run(() => full ? Disassembly.Full(bin, file) : Disassembly.Functions(bin, file));
            }
            finally
            {
                IsBusy = false;
                ProgressText = "";
            }
        }
        ShowDocument(new DisassemblyViewModel(file, File.ReadAllBytes(bin.FileName), full, bin));
    }

    /// <summary>View file in hex: the bin, and the imported SRAM snapshot next to it.</summary>
    [RelayCommand]
    private void ViewHex()
    {
        if (Binary is not T7Binary bin) return;
        ShowDocument(new HexViewerViewModel(bin.FileName, bin));
        if (SramFile is { } ram && File.Exists(ram)) ShowDocument(new HexViewerViewModel(ram, bin, sram: true));
    }

    /// <summary>Actions → Airmass result viewer, when the bin has the tables it needs (T7Suite silently did nothing otherwise).</summary>
    [RelayCommand]
    private void ShowAirmassResult()
    {
        if (Binary is not T7Binary bin) return;
        if (!AirmassResult.Available(bin))
        {
            ShowInfo("This file lacks the pedal, torque or airmass tables the airmass result viewer needs");
            return;
        }
        ShowDocument(new AirmassResultViewModel(this, bin));
    }

    // ---- map menus ----

    public string MyMapsFile => Path.Combine(SettingsKey.Folder(Suite), "mymaps.xml");

    /// <summary>My Maps changed: the view rebuilds its menu.</summary>
    public event Action? MyMapsChanged;

    public void SaveMyMaps(IEnumerable<MapShortcut> maps)
    {
        MapMenus.SaveMyMaps(MyMapsFile, maps);
        MyMapsChanged?.Invoke();
    }

    public void AddToMyMaps(SymbolHelper sh)
    {
        MapMenus.AddToMyMaps(MyMapsFile, sh.Varname);
        MyMapsChanged?.Invoke();
    }

    /// <summary>A map button (quick maps / My Maps). The AFR feedback maps of My Maps need the realtime features.</summary>
    [RelayCommand]
    private void OpenShortcut(MapShortcut shortcut)
    {
        if (shortcut.Symbol is "targetafr" or "feedbackafr")
        {
            ShowInfo("The AFR maps come with the realtime features.");
            return;
        }
        OpenSymbolByName(shortcut.Symbol);
    }

    /// <summary>A descriptor import changed names: the list shows them (the import saved &lt;bin&gt;.xml).</summary>
    public void ImportSymbols(Action<T7Binary> import)
    {
        if (Binary is not T7Binary bin) return;
        try
        {
            import(bin);
        }
        catch (Exception e) when (e is IOException or System.Data.DataException or System.Xml.XmlException)
        {
            ShowInfo("Failed to import: " + e.Message);
        }
        Symbols?.Refresh();
    }

    /// <summary>Search map content: "No results found..." or a results tab.</summary>
    public void SearchMaps(MapSearchOptions options)
    {
        if (Binary is not T7Binary bin) return;
        List<SymbolHelper> hits = MapSearch.Find(bin, options);
        if (hits.Count == 0) ShowInfo("No results found...");
        else ShowDocument(new SearchResultsViewModel(this, bin.FileName, hits));
    }

    // ---- compare ----

    /// <summary>"Compare symbols with other binary": the results open as a tab.</summary>
    public async Task CompareToFileAsync(string otherFile)
    {
        if (Binary is not T7Binary bin || bin.Symbols.Count == 0) return;
        if (!T7Binary.IsValidFile(otherFile))
        {
            ShowInfo("File is not a Trionic 7 binary file!");
            return;
        }
        IsBusy = true;
        try
        {
            var (other, rows) = await Task.Run(() =>
            {
                T7Binary o = T7Binary.Open(otherFile, Settings.ApplicationLanguage, false);
                return (o, T7Compare.Compare(bin, o, Settings.ApplicationLanguage));
            });
            ShowDocument(CompareResultsViewModel.Binaries(this, bin, other, rows));
        }
        finally
        {
            IsBusy = false;
            ProgressText = "";
        }
    }

    /// <summary>"Compare to original file": the stock bin with this part number, when Binaries has exactly one.</summary>
    public string? OriginalFile => Binary is T7Binary bin ? T7Compare.OriginalFile(bin) : null;

    [RelayCommand]
    private Task CompareToOriginal() => OriginalFile is { } file ? CompareToFileAsync(file) : Task.CompletedTask;

    /// <summary>Transfer maps: the selection remembered between runs (T7Suite's TransferSettings key).</summary>
    public HashSet<string> LastTransferSelection()
    {
        using var key = SettingsKey.Open(Suite, "TransferSettings");
        return key.GetValueNames().ToHashSet();
    }

    public List<string> TransferMaps(string target, HashSet<string> selected)
    {
        using (var key = SettingsKey.Open(Suite, "TransferSettings"))
        {
            foreach (string old in key.GetValueNames()) key.DeleteValue(old);
            foreach (string name in selected) key.SetValue(name, "1");
        }
        int before = TransactionLog?.TransCollection.Count ?? 0;
        List<string> report = T7Compare.TransferMaps((T7Binary)Binary!, target, selected, Settings.ApplicationLanguage, Settings.AutoFixFooter, TransactionLog);
        TransactionsAdded(before);
        return report;
    }

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
