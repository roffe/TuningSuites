using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CommonSuite;
using SuiteApp.Controls;
using SuiteApp.Services;
using SuiteApp.ViewModels;

namespace SuiteApp.Views;

/// <summary>
/// The offline tuning menus every ECU has: compare and transfer, search, symbol imports and exports, tuning packages, part
/// number lookup and the menus made in code (a MenuItem named QuickMapsMenu / MyMapsMenu / RecentMenu in the program's menus).
/// </summary>
public partial class SuiteMainView
{
    /// <summary>The compare, transfer and copy dialogs' filter name.</summary>
    protected virtual string CompareFilesName => "binary files";

    private void WatchMapMenus(MainWindowViewModel vm)
    {
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.Binary)) BuildTuningMenu();
            // My Maps are the ECU's: its map names
            else if (e.PropertyName == nameof(MainWindowViewModel.Module)) BuildMyMaps();
        };
        vm.MyMapsChanged += BuildMyMaps;
        BuildMyMaps();
        vm.Recent.CollectionChanged += (_, _) => BuildRecent();
        BuildRecent();
    }

    // File > Recent: a menu item per file, as the other menus made in code (not a bound list: what reads the menu's items finds them)
    private void BuildRecent()
    {
        if (this.FindControl<MenuItem>("RecentMenu") is not { } menu) return;
        menu.Items.Clear();
        foreach (RecentFile file in Vm.Recent)
            menu.Items.Add(new MenuItem { Header = MenuTextConverter.Escape(file.Name), Command = Vm.OpenRecentCommand, CommandParameter = file, [ToolTip.TipProperty] = file.Path });
    }

    // ---- compare ----

    /// <summary>T5Suite's compare took several files at once (a compare list); T7Suite's and T8Suite's one.</summary>
    protected virtual bool CompareSeveralFiles => false;

    protected async void OnCompareToFile(object? sender, RoutedEventArgs e)
    {
        const string title = "Compare symbols with other binary";
        if (CompareSeveralFiles) await Vm.CompareToFilesAsync(await Dialogs.OpenFiles(Host, title, CompareFilesName, ["*.bin"]));
        else if (await Dialogs.OpenFile(Host, title, CompareFilesName, ["*.bin"]) is { } file) await Vm.CompareToFileAsync(file);
    }

    protected void OnBinaryCompare(object? sender, RoutedEventArgs e) => BinaryCompare(false);

    /// <summary>T8Suite's "Compare binary outside symbolrange": only lines where bytes outside the open file's symbols differ.</summary>
    protected void OnBinaryCompareOutsideSymbols(object? sender, RoutedEventArgs e) => BinaryCompare(true);

    private async void BinaryCompare(bool outsideSymbols)
    {
        string title = outsideSymbols ? "Compare binary outside symbolrange" : "Compare binary with other binary";
        if (Vm.Binary is not { } bin || await Dialogs.OpenFile(Host, title, CompareFilesName, ["*.bin"]) is not { } file) return;
        var lines = SuiteCompare.BinaryDiff(bin.FileName, file, outsideSymbols ? bin.Symbols : null);
        var text = new StringBuilder($"{Path.GetFileName(bin.FileName)} / {Path.GetFileName(file)}: {lines.Count} lines differ\n\n");
        foreach (var (mine, theirs) in lines) text.Append(mine).Append('\n').Append(theirs).Append("\n\n");
        await Dialogs.Text(Host, "Binary compare", text.ToString());
    }

    /// <summary>The transfer wizard's caption and welcome text (T7Suite's / T8Suite's).</summary>
    protected virtual (string caption, string text) TransferWizard =>
        ("Transfer maps to different binary wizard", "This wizard assists you in transferring map contents from the current file to another binary.\n\n"
            + "Make sure engine types and such are equal for both binaries!\n\n"
            + "The author does not take responsibility for any damage done to your car or other objects in any form!\n\n"
            + "Select the target binary now?");

    protected async void OnTransferMaps(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is not { } bin) return;
        var (caption, text) = TransferWizard;
        if (!await Dialogs.YesNo(Host, text, caption)) return;
        if (await Dialogs.OpenFile(Host, "Transfer maps to another binary", CompareFilesName, ["*.bin"]) is not { } target) return;
        var selection = new TransferSelectionViewModel(SuiteCompare.TransferCandidates(bin), Vm.LastTransferSelection());
        if (!await new TransferSelectionWindow { DataContext = selection }.ShowDialog<bool>(Host)) return;
        var report = Vm.TransferMaps(target, selection.Selection);
        await Dialogs.Text(Host, "Data transfer report", string.Join('\n', report));
    }

    protected async void OnCopyAddressTable(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is not { } bin
            || await Dialogs.OpenFile(Host, "Select binary file to transfer the address table to...", CompareFilesName, ["*.bin"]) is not { } target) return;
        if (bin.AddressTableStart(bin.FileName) != bin.AddressTableStart(target)
            && !await Dialogs.YesNo(Host, "Address table start addresses are not equal, continue anyway?", "Attention!"))
        {
            await Dialogs.Info(Host, "Transfer cancelled");
            return;
        }
        try
        {
            bin.CopyAddressTable(target);
            await Dialogs.Info(Host, "Transfer done");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await Dialogs.Info(Host, ex.Message);
        }
    }

    protected async void OnSearchMaps(object? sender, RoutedEventArgs e)
    {
        var options = new SearchMapsViewModel();
        if (Vm.Binary != null && await new SearchMapsWindow { DataContext = options }.ShowDialog<bool>(Host)) Vm.SearchMaps(options.ToOptions());
    }

    /// <summary>The lookup's partnumber list (frmPartNumberList, a tab per ECU): the picked partnumber, null when cancelled.</summary>
    protected virtual Func<Task<string?>>? BrowsePartNumbers => null;

    protected async void OnLookupPartnumber(object? sender, RoutedEventArgs e)
    {
        if (await ShowPartLookupAsync(Host) is { } pick) await RunPartLookupAsync(pick.action, pick.stock);
    }

    /// <summary>
    /// Lookup partnumber over owner, looked up at once when a partnumber is given: the action picked on its stock file, or null. Every
    /// ECU's partnumbers; Compare to this file only for the open file's ECU's.
    /// </summary>
    protected async Task<(string action, string stock)?> ShowPartLookupAsync(Window owner, string partNumber = "")
    {
        var lookup = new PartLookupViewModel(Vm.LookupAnyPartNumber, Vm.Caption)
        {
            Own = pn => Vm.Binary != null ? Vm.LookupPartNumber(pn) : null, Browse = BrowsePartNumbers, PartNumber = partNumber,
        };
        if (partNumber != "") lookup.Lookup();
        string? action = await new PartLookupWindow { DataContext = lookup }.ShowDialog<string?>(owner);
        return action != null && lookup.Info?.Binary is { } stock ? (action, stock) : null;
    }

    /// <summary>The lookup's Open this file, Compare to this file or Create new from original ("create:&lt;file&gt;").</summary>
    protected async Task RunPartLookupAsync(string action, string stock)
    {
        if (action == "open") await Vm.OpenFileAsync(stock, true);
        else if (action == "compare") await Vm.CompareToFileAsync(stock);
        else if (action.StartsWith("create:"))
        {
            string file = action[7..];
            File.Copy(stock, file, true);
            await Vm.OpenFileAsync(file, true);
        }
    }

    // ---- imports and exports ----

    protected async void OnImportXml(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.OpenFile(Host, "Import XML descriptor", "XML documents", ["*.xml"]) is { } file) ImportSymbols(bin => SymbolFiles.ImportXml(bin, file));
    }

    protected async void OnImportCsv(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.OpenFile(Host, "Import CSV descriptor", "CSV documents", ["*.csv"]) is { } file) ImportSymbols(bin => SymbolFiles.ImportCsv(bin, file));
    }

    protected async void OnImportAs2(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.OpenFile(Host, "Import AS2 descriptor", "AS2 documents", ["*.as2"]) is { } file) ImportSymbols(bin => SymbolFiles.ImportAs2(bin, file));
    }

    protected async void OnExportS19(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is { } bin && await Dialogs.SaveFile(Host, "Export file to motorola S19 format...", "S19 files", "S19",
                Path.ChangeExtension(Path.GetFileName(bin.FileName), ".S19")) is { } target)
            SymbolFiles.ExportS19(bin, target);
    }

    protected async void OnExportMapCsv(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is not { } bin) return;
        if (Vm.SelectedSymbol is not { } sh)
        {
            await Dialogs.Info(Host, "No symbol selected in the primary symbol list", Vm.Caption);
            return;
        }
        string name = Path.GetFileName(bin.FileName) + "~" + sh.SmartVarname + ".csv";
        if (await Dialogs.SaveFile(Host, "Export map to CSV", "CSV files", "csv", name) is { } target) SymbolFiles.ExportMapCsv(bin, sh, target);
    }

    /// <summary>T5Suite's Import map from Excel: the map from the name after the last '~', else the selected symbol.</summary>
    protected async void OnImportMapCsv(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is not { } bin || await Dialogs.OpenFile(Host, "Import map from CSV", "CSV files", ["*.csv"]) is not { } file) return;
        string stem = Path.GetFileNameWithoutExtension(file);
        if (stem.Contains('~') && bin.Find(stem[(stem.LastIndexOf('~') + 1)..]) is { } named)
        {
            if (await Dialogs.YesNo(Host, $"Found valid symbol for import: {named.SmartVarname}. Are you sure you want to overwrite the map in the binary?", "Confirmation"))
                Vm.ImportMapCsv(named, file);
        }
        else if (Vm.SelectedSymbol is { } sh)
        {
            if (await Dialogs.YesNo(Host, $"Import the file into {sh.SmartVarname}? Are you sure you want to overwrite the map in the binary?", "Confirmation"))
                Vm.ImportMapCsv(sh, file);
        }
        else await Dialogs.Info(Host, "No symbol selected in the primary symbol list", Vm.Caption);
    }

    protected async void OnGenerateIdc(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is { } bin) await Dialogs.Info(Host, "Idc file written: " + bin.ExportIdc(), Vm.Caption);
    }

    protected async void OnExportSymbolCsv(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is { } bin && await Dialogs.SaveFile(Host, "Select a filename to save the symbollist", "CSV files", "csv", null) is { } target)
        {
            SymbolFiles.ExportSymbolCsv(bin, target, Vm.SymbolCsvUserDescription);
            await Dialogs.Info(Host, "Export done", Vm.Caption);
        }
    }

    // ---- tuning packages ----

    protected async void OnImportTuningPackage(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.OpenFile(Host, "Import tuning package", Vm.PackageFilesName, ["*." + Vm.PackageExtension]) is { } file
            && Vm.ImportTuningPackage(file) is { } results)
            await new ImportResultsWindow(results).ShowDialog(Host);
    }

    protected async void OnExportPackage(object? sender, RoutedEventArgs e)
    {
        var selected = Vm.SelectedSymbols.ToList();
        if (Vm.Binary is { } bin && selected.Count > 0
            && await Dialogs.SaveFile(Host, "Export as tuning package", Vm.PackageFilesName, Vm.PackageExtension, null) is { } target)
            SymbolFiles.ExportPackage(bin, selected, target);
    }

    protected async void OnExportFixedPackage(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is { } bin && await Dialogs.SaveFile(Host, "Export fixed tuning package", Vm.PackageFilesName, Vm.PackageExtension, null) is { } target)
            SymbolFiles.ExportPackage(bin, SymbolFiles.FixedPackage(bin), target);
    }

    // ---- map menus ----

    private MenuItem Shortcut(MapShortcut m) =>
        new() { Header = MenuTextConverter.Escape(m.Caption), Command = Vm.OpenShortcutCommand, CommandParameter = m, [ToolTip.TipProperty] = m.Symbol };

    // a descriptor import names symbols: the Tuning menu then shows the maps that have their names now
    private void ImportSymbols(Action<SuiteBinary> import)
    {
        Vm.ImportSymbols(import);
        BuildTuningMenu();
    }

    private List<object?>? m_tuningItems;

    // the menu's own items (the tuning wizards), then txlogger's menu for the ECU with what this bin and suite have
    private void BuildTuningMenu()
    {
        if (this.FindControl<MenuItem>("QuickMapsMenu") is not { } menu) return;
        m_tuningItems ??= menu.Items.ToList();
        menu.Items.Clear();
        foreach (object? item in m_tuningItems) menu.Items.Add(item);
        if (Vm.Binary is { } bin)
            foreach (MenuItem item in TuningItems(bin, bin.TuningMenu()))
            {
                // the ribbon's groups, as the old suites' Tuning pages had them
                if (item.ItemCount > 0) Ribbon.SetGroup(item, Ribbon.Text(item.Header));
                menu.Items.Add(item);
            }
    }

    // left out: a map the bin has under none of its names, one opening what an entry above it does, an empty submenu
    private List<MenuItem> TuningItems(SuiteBinary bin, IEnumerable<TuningItem> entries)
    {
        var items = new List<MenuItem>();
        var opened = new HashSet<string>();
        foreach (TuningItem entry in entries)
        {
            var item = new MenuItem { Header = MenuTextConverter.Escape(entry.Caption) };
            if (entry.Children != null)
            {
                foreach (MenuItem child in TuningItems(bin, entry.Children)) item.Items.Add(child);
                if (item.Items.Count == 0) continue;
            }
            else
            {
                string[] maps = entry.Maps(bin);
                if (maps.Length == 0 || !opened.Add(string.Join('|', maps))) continue;
                // named by its symbol: the name it opens
                if (entry.Symbols?[0] == entry.Caption) item.Header = MenuTextConverter.Escape(string.Join(" + ", maps));
                // a map that lives in the ECU (an adaption or counter): read from SRAM as the Realtime menu does, never the file
                item.Command = entry.Ecu ? Vm.ShowEcuMapCommand : Vm.OpenMapsCommand;
                item.CommandParameter = entry.Ecu ? maps[0] : maps;
                ToolTip.SetTip(item, string.Join('\n', maps));
            }
            items.Add(item);
        }
        return items;
    }

    private void BuildMyMaps()
    {
        if (this.FindControl<MenuItem>("MyMapsMenu") is not { } menu) return;
        menu.Items.Clear();
        var define = new MenuItem { Header = "Define myMaps..." };
        define.Click += OnDefineMyMaps;
        menu.Items.Add(define);
        var maps = MapMenus.LoadMyMaps(Vm.MyMapsFile);
        if (maps.Count > 0) menu.Items.Add(new Separator());
        foreach (var group in maps.GroupBy(m => m.Group))
        {
            // a ribbon group per category, as T7Suite's My Maps page
            var item = new MenuItem { Header = MenuTextConverter.Escape(group.Key), [Ribbon.GroupProperty] = group.Key };
            foreach (var m in group) item.Items.Add(Shortcut(m));
            menu.Items.Add(item);
        }
    }

    private async void OnDefineMyMaps(object? sender, RoutedEventArgs e)
    {
        var maps = new MyMapsViewModel(Vm.MyMapsToEdit());
        if (await new MyMapsWindow { DataContext = maps }.ShowDialog<bool>(Host)) Vm.SaveMyMaps(maps.Maps);
    }

    protected void OnAddToMyMaps(object? sender, RoutedEventArgs e)
    {
        if (Vm.SelectedSymbol is { } sh) Vm.AddToMyMaps(sh);
    }
}
