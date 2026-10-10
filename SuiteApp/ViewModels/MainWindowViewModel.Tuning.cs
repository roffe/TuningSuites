using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommonSuite;
using CommunityToolkit.Mvvm.Input;

namespace SuiteApp.ViewModels;

/// <summary>The offline tuning both suites have: compare and transfer maps, search, symbol imports, tuning packages, map menus.</summary>
public abstract partial class MainWindowViewModel
{
    /// <summary>The package dialogs' filter: "Trionic 7 packages".</summary>
    public abstract string PackageFilesName { get; }

    /// <summary>"t7p" / "t8p".</summary>
    public abstract string PackageExtension { get; }

    /// <summary>A file to compare with, opened without changing it.</summary>
    protected virtual SuiteBinary OpenCompareBinary(string path) => OpenBinary(path);

    /// <summary>Export symbollist as CSV writes the user description too (T7Suite; T8Suite didn't).</summary>
    public virtual bool SymbolCsvUserDescription => true;

    /// <summary>The search results' tab: "Search results: &lt;file&gt;" (T7Suite).</summary>
    protected virtual string SearchTitle(SuiteBinary bin, MapSearchOptions options) => "Search results: " + Path.GetFileName(bin.FileName);

    /// <summary>The last transfer selection the old suite kept outside HKCU\Software\MattiasC, imported once; null when it didn't.</summary>
    protected virtual string? LegacyTransferKey => null;

    /// <summary>Lookup partnumber: what the entered part number is, null when the suite doesn't know it.</summary>
    public abstract PartInfo? LookupPartNumber(string partNumber);

    /// <summary>Lookup partnumber shows power, torque and the engine boxes (T7Suite).</summary>
    public virtual bool PartDetails => true;

    /// <summary>What Define myMaps starts with when there is no mymaps.xml yet.</summary>
    public abstract IReadOnlyList<MapShortcut> MyMapsDefaults { get; }

    // ---- compare ----

    /// <summary>"Compare symbols with other binary": the results open as a tab.</summary>
    public async Task CompareToFileAsync(string otherFile)
    {
        if (Binary is not { } bin || bin.Symbols.Count == 0) return;
        if (!await Task.Run(() => IsValidFile(otherFile)))
        {
            if (InvalidFileMessage is { } message) ShowInfo(message);
            return;
        }
        IsBusy = true;
        try
        {
            var (other, rows) = await Task.Run(() =>
            {
                SuiteBinary o = OpenCompareBinary(otherFile);
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
        using var key = SettingsKey.Open(Suite, "TransferSettings");
        var names = key.GetValueNames().ToHashSet();
        if (names.Count == 0 && LegacyTransferKey is { } legacy)
        {
            names = SettingsKey.ImportRegistry(legacy).Keys.Where(n => !n.Contains('\\')).ToHashSet();
            foreach (string name in names) key.SetValue(name, "1");
        }
        return names;
    }

    public List<string> TransferMaps(string target, HashSet<string> selected)
    {
        using (var key = SettingsKey.Open(Suite, "TransferSettings"))
        {
            foreach (string old in key.GetValueNames()) key.DeleteValue(old);
            foreach (string name in selected) key.SetValue(name, "1");
        }
        int before = TransactionLog?.TransCollection.Count ?? 0;
        List<string> report = SuiteCompare.TransferMaps(Binary!, target, OpenBinary, selected, TransactionLog);
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
        else ShowDocument(new SearchResultsViewModel(this, SearchTitle(bin, options), hits));
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
        Symbols?.Refresh();
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

    public string MyMapsFile => Path.Combine(SettingsKey.Folder(Suite), "mymaps.xml");

    /// <summary>My Maps changed: the view rebuilds its menu.</summary>
    public event Action? MyMapsChanged;

    /// <summary>Define myMaps' rows: the file, or the suite's defaults when there is none yet.</summary>
    public List<MapShortcut> MyMapsToEdit() => File.Exists(MyMapsFile) ? MapMenus.LoadMyMaps(MyMapsFile) : MyMapsDefaults.ToList();

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

    /// <summary>A map button (quick maps / My Maps).</summary>
    [RelayCommand]
    private void OpenShortcut(MapShortcut shortcut)
    {
        if (!OpenSpecialShortcut(shortcut)) OpenSymbolByName(shortcut.Symbol);
    }

    /// <summary>A My Maps entry that isn't a symbol (T7Suite's AFR maps); true when handled.</summary>
    protected virtual bool OpenSpecialShortcut(MapShortcut shortcut) => false;
}
