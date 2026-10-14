using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommonSuite;
using CommunityToolkit.Mvvm.Input;

namespace SuiteApp.ViewModels;

/// <summary>The offline tuning every ECU has: compare and transfer maps, search, symbol imports, tuning packages, map menus.</summary>
public abstract partial class MainWindowViewModel
{
    /// <summary>The package dialogs' filter: "Trionic 7 packages".</summary>
    public string PackageFilesName => Module.PackageFilesName;

    /// <summary>"t7p" / "t8p".</summary>
    public string PackageExtension => Module.PackageExtension;

    /// <summary>Export symbollist as CSV writes the user description too (T7Suite; T8Suite didn't).</summary>
    public bool SymbolCsvUserDescription => Module.SymbolCsvUserDescription;

    /// <summary>Lookup partnumber: what the entered part number is for the open file's ECU, null when it doesn't know it.</summary>
    public PartInfo? LookupPartNumber(string partNumber) => Module.LookupPartNumber(partNumber);

    /// <summary>Lookup partnumber over every ECU's partnumbers (no partnumber is in two of their lists).</summary>
    public PartInfo? LookupAnyPartNumber(string partNumber) => Modules.Select(m => m.LookupPartNumber(partNumber)).FirstOrDefault(info => info != null);

    // ---- compare ----

    /// <summary>Compare with several binaries (T5Suite): one goes straight to its results, more to a compare list.</summary>
    public async Task CompareToFilesAsync(IReadOnlyList<string> files)
    {
        if (Binary is not { } bin || files.Count == 0) return;
        if (files.Count == 1)
        {
            await CompareToFileAsync(files[0]);
            return;
        }
        IsBusy = true;
        try
        {
            ShowDocument(new CompareListViewModel(this, bin.FileName, await Task.Run(() => CompareListViewModel.Count(bin, files, Module.OpenCompareBinary))));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>"Compare symbols with other binary": the results open as a tab.</summary>
    public async Task CompareToFileAsync(string otherFile)
    {
        otherFile = LinkTarget(otherFile);
        if (Binary is not { } bin || bin.Symbols.Count == 0) return;
        EcuModule ecu = Module;
        if (!await Task.Run(() => ecu.IsValidFile(otherFile)))
        {
            if (ecu.InvalidFileMessage is { } message) ShowInfo(message);
            return;
        }
        IsBusy = true;
        try
        {
            var (other, rows) = await Task.Run(() =>
            {
                SuiteBinary o = ecu.OpenCompareBinary(otherFile);
                return (o, SuiteCompare.Compare(bin, o));
            });
            ShowDocument(CompareResultsViewModel.Binaries(this, bin, other, rows));
        }
        finally
        {
            IsBusy = false;
            ProgressText = "";
        }
    }

    /// <summary>Transfer maps: the selection remembered between runs (the old TransferSettings key).</summary>
    public HashSet<string> LastTransferSelection()
    {
        using var key = SettingsKey.Open(Module.SettingsPath, "TransferSettings");
        var names = key.GetValueNames().ToHashSet();
        if (names.Count == 0 && Module.LegacyTransferKey is { } legacy)
        {
            names = SettingsKey.ImportRegistry(legacy).Keys.Where(n => !n.Contains('\\')).ToHashSet();
            foreach (string name in names) key.SetValue(name, "1");
        }
        return names;
    }

    public List<string> TransferMaps(string target, HashSet<string> selected)
    {
        using (var key = SettingsKey.Open(Module.SettingsPath, "TransferSettings"))
        {
            foreach (string old in key.GetValueNames()) key.DeleteValue(old);
            foreach (string name in selected) key.SetValue(name, "1");
        }
        int before = TransactionLog?.TransCollection.Count ?? 0;
        List<string> report = SuiteCompare.TransferMaps(Binary!, target, Module.OpenBinary, selected, TransactionLog);
        TransactionsAdded(before);
        return report;
    }

    /// <summary>Import map from CSV: the map written (a transaction entry in a project), the checksum updated, open viewers refreshed.</summary>
    public void ImportMapCsv(SymbolHelper sh, string file)
    {
        if (Binary is not { } bin || bin.FileAddress(sh) is not (var address and >= 0)) return;
        int before = TransactionLog?.TransCollection.Count ?? 0;
        try
        {
            bin.WriteData(address, SymbolFiles.ImportMapCsv(bin, sh, file), TransactionLog, "Import map from CSV");
            bin.UpdateChecksum();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowInfo(e is InvalidDataException ? e.Message : "Failed to import: " + e.Message);
        }
        TransactionsAdded(before);
        RefreshViewers(bin.FileName);
    }

    /// <summary>Search map content: "No results found..." or a results tab.</summary>
    public void SearchMaps(MapSearchOptions options)
    {
        if (Binary is not { } bin) return;
        List<SymbolHelper> hits = MapSearch.Find(bin, options);
        if (hits.Count == 0) ShowInfo("No results found...");
        else ShowDocument(new SearchResultsViewModel(this, Module.SearchTitle(bin, options), hits));
    }

    /// <summary>A descriptor import changed names: the list shows them (the import saved &lt;bin&gt;.xml).</summary>
    public void ImportSymbols(Action<SuiteBinary> import)
    {
        if (Binary is not { } bin) return;
        try
        {
            import(bin);
        }
        catch (Exception e) when (e is IOException or System.Data.DataException or System.Xml.XmlException)
        {
            ShowInfo("Failed to import: " + e.Message);
        }
        RefreshSymbols();
    }

    // ---- tuning packages ----

    /// <summary>
    /// File → Import tuning package: the package's symbols and search &amp; replace patterns into the bin (transaction entries
    /// in a project), one checksum update; open viewers show the new data. The results for the "Import results" list.
    /// </summary>
    public List<PackageResult>? ImportTuningPackage(string file)
    {
        if (Binary is not { } bin) return null;
        int before = TransactionLog?.TransCollection.Count ?? 0;
        try
        {
            List<PackageResult> results = TuningPackage.Read(file, bin).Apply(bin, TransactionLog);
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

    /// <summary>File → Edit a tuning package (one at a time, as both suites).</summary>
    [RelayCommand]
    private void EditTuningPackage()
    {
        if (Binary is not { } bin) return;
        if (Viewers.OfType<TuningPackageEditorViewModel>().FirstOrDefault() is { } open)
        {
            SelectedViewer = open;
            return;
        }
        ShowDocument(new TuningPackageEditorViewModel(this, bin));
    }

    // ---- map menus ----

    /// <summary>The open file's ECU's My Maps (its map names): mymaps.xml in its settings folder.</summary>
    public string MyMapsFile => Path.Combine(SettingsKey.Folder(Module.SettingsPath), "mymaps.xml");

    /// <summary>My Maps changed: the view rebuilds its menu.</summary>
    public event Action? MyMapsChanged;

    /// <summary>Define myMaps' rows: the file, or the ECU's defaults when there is none yet.</summary>
    public List<MapShortcut> MyMapsToEdit() => File.Exists(MyMapsFile) ? MapMenus.LoadMyMaps(MyMapsFile) : Module.MyMapsDefaults.ToList();

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

    /// <summary>A My Maps entry.</summary>
    [RelayCommand]
    private void OpenShortcut(MapShortcut shortcut)
    {
        if (!Module.OpenSpecialShortcut(shortcut)) OpenSymbolByName(shortcut.Symbol);
    }

    /// <summary>A Tuning menu entry (txlogger's menu): each of its maps in its own viewer.</summary>
    [RelayCommand]
    private void OpenMaps(string[] maps)
    {
        foreach (string map in maps) OpenSymbolByName(map);
    }
}
