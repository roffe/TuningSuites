using System;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CommonSuite;
using SuiteApp.Services;
using SuiteApp.ViewModels;

namespace SuiteApp.Views;

/// <summary>
/// The offline tuning menus both suites have: compare and transfer, search, symbol imports and exports, tuning packages, part
/// number lookup and the map menus (a MenuItem named QuickMapsMenu / MyMapsMenu in the app's menus).
/// </summary>
public partial class SuiteMainWindow
{
    /// <summary>The compare, transfer and copy dialogs' filter name.</summary>
    protected virtual string CompareFilesName => "binary files";

    private void WatchMapMenus(MainWindowViewModel vm)
    {
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MainWindowViewModel.Binary)) BuildQuickMaps(); };
        vm.MyMapsChanged += BuildMyMaps;
        BuildMyMaps();
    }

    // ---- compare ----

    protected async void OnCompareToFile(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.OpenFile(this, CompareFilesName, "*.bin") is { } file) await Vm.CompareToFileAsync(file);
    }

    protected void OnBinaryCompare(object? sender, RoutedEventArgs e) => BinaryCompare(false);

    /// <summary>T8Suite's "Compare binary outside symbolrange": only lines where bytes outside the open file's symbols differ.</summary>
    protected void OnBinaryCompareOutsideSymbols(object? sender, RoutedEventArgs e) => BinaryCompare(true);

    private async void BinaryCompare(bool outsideSymbols)
    {
        if (Vm.Binary is not { } bin || await Dialogs.OpenFile(this, CompareFilesName, "*.bin") is not { } file) return;
        var lines = SuiteCompare.BinaryDiff(bin.FileName, file, outsideSymbols ? bin.Symbols : null);
        var text = new StringBuilder($"{Path.GetFileName(bin.FileName)} / {Path.GetFileName(file)}: {lines.Count} lines differ\n\n");
        foreach (var (mine, theirs) in lines) text.Append(mine).Append('\n').Append(theirs).Append("\n\n");
        await Dialogs.Text(this, "Binary compare", text.ToString());
    }

    protected async void OnTransferMaps(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is not { } bin) return;
        const string text = "This wizard assists you in transferring map contents from the current file to another binary.\n\n"
            + "Make sure engine types and such are equal for both binaries!\n\n"
            + "The author does not take responsibility for any damage done to your car or other objects in any form!\n\n"
            + "Select the target binary now?";
        if (!await Dialogs.YesNo(this, text, "Transfer maps to different binary wizard")) return;
        if (await Dialogs.OpenFile(this, CompareFilesName, "*.bin") is not { } target) return;
        var selection = new TransferSelectionViewModel(SuiteCompare.TransferCandidates(bin), Vm.LastTransferSelection());
        if (!await new TransferSelectionWindow { DataContext = selection }.ShowDialog<bool>(this)) return;
        var report = Vm.TransferMaps(target, selection.Selection);
        await Dialogs.Text(this, "Data transfer report", string.Join('\n', report));
    }

    protected async void OnCopyAddressTable(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is not { } bin || await Dialogs.OpenFile(this, CompareFilesName, "*.bin") is not { } target) return;
        if (bin.AddressTableStart(bin.FileName) != bin.AddressTableStart(target)
            && !await Dialogs.YesNo(this, "Address table start addresses are not equal, continue anyway?", "Attention!"))
        {
            await Dialogs.Info(this, "Transfer cancelled");
            return;
        }
        try
        {
            bin.CopyAddressTable(target);
            await Dialogs.Info(this, "Transfer done");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await Dialogs.Info(this, ex.Message);
        }
    }

    protected async void OnSearchMaps(object? sender, RoutedEventArgs e)
    {
        var options = new SearchMapsViewModel();
        if (Vm.Binary != null && await new SearchMapsWindow { DataContext = options }.ShowDialog<bool>(this)) Vm.SearchMaps(options.ToOptions());
    }

    protected async void OnLookupPartnumber(object? sender, RoutedEventArgs e)
    {
        var lookup = new PartLookupViewModel(Vm.LookupPartNumber, Vm.PartDetails, Vm.Caption);
        string? action = await new PartLookupWindow { DataContext = lookup }.ShowDialog<string?>(this);
        if (action == null || lookup.Info?.Binary is not { } stock) return;
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
        if (await Dialogs.OpenFile(this, "XML documents", "*.xml") is { } file) Vm.ImportSymbols(bin => SymbolFiles.ImportXml(bin, file));
    }

    protected async void OnImportCsv(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.OpenFile(this, "CSV documents", "*.csv") is { } file) Vm.ImportSymbols(bin => SymbolFiles.ImportCsv(bin, file));
    }

    protected async void OnImportAs2(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.OpenFile(this, "AS2 documents", "*.as2") is { } file) Vm.ImportSymbols(bin => SymbolFiles.ImportAs2(bin, file));
    }

    protected async void OnExportS19(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is { } bin && await Dialogs.SaveFile(this, "S19 files", "S19", Path.ChangeExtension(Path.GetFileName(bin.FileName), ".S19")) is { } target)
            SymbolFiles.ExportS19(bin, target);
    }

    protected async void OnExportMapCsv(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is not { } bin) return;
        if (Vm.SelectedSymbol is not { } sh)
        {
            await Dialogs.Info(this, "No symbol selected in the primary symbol list", Vm.Caption);
            return;
        }
        string name = Path.GetFileName(bin.FileName) + "~" + sh.SmartVarname + ".csv";
        if (await Dialogs.SaveFile(this, "CSV files", "csv", name) is { } target) SymbolFiles.ExportMapCsv(bin, sh, target);
    }

    protected async void OnGenerateIdc(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is { } bin) await Dialogs.Info(this, "Idc file written: " + bin.ExportIdc(), Vm.Caption);
    }

    protected async void OnExportSymbolCsv(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is { } bin && await Dialogs.SaveFile(this, "CSV files", "csv") is { } target)
        {
            SymbolFiles.ExportSymbolCsv(bin, target, Vm.SymbolCsvUserDescription);
            await Dialogs.Info(this, "Export done", Vm.Caption);
        }
    }

    // ---- tuning packages ----

    protected async void OnImportTuningPackage(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.OpenFile(this, Vm.PackageFilesName, "*." + Vm.PackageExtension) is { } file && Vm.ImportTuningPackage(file) is { } results)
            await new ImportResultsWindow(results).ShowDialog(this);
    }

    protected async void OnExportPackage(object? sender, RoutedEventArgs e)
    {
        var selected = Vm.SelectedSymbols.ToList();
        if (Vm.Binary is { } bin && selected.Count > 0 && await Dialogs.SaveFile(this, Vm.PackageFilesName, Vm.PackageExtension) is { } target)
            SymbolFiles.ExportPackage(bin, selected, target);
    }

    protected async void OnExportFixedPackage(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is { } bin && await Dialogs.SaveFile(this, Vm.PackageFilesName, Vm.PackageExtension) is { } target)
            SymbolFiles.ExportPackage(bin, SymbolFiles.FixedPackage(bin), target);
    }

    // ---- map menus ----

    private MenuItem Shortcut(MapShortcut m) =>
        new() { Header = MenuTextConverter.Escape(m.Caption), Command = Vm.OpenShortcutCommand, CommandParameter = m, [ToolTip.TipProperty] = m.Symbol };

    // the ribbon's map buttons, grouped as the ribbon was, the ones this bin has (DynamicTuningMenu)
    private void BuildQuickMaps()
    {
        if (this.FindControl<MenuItem>("QuickMapsMenu") is not { } menu) return;
        menu.Items.Clear();
        foreach (var group in (Vm.Binary?.QuickMaps() ?? []).GroupBy(m => m.Group))
        {
            var item = new MenuItem { Header = MenuTextConverter.Escape(group.Key) };
            foreach (var m in group) item.Items.Add(Shortcut(m));
            menu.Items.Add(item);
        }
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
            var item = new MenuItem { Header = MenuTextConverter.Escape(group.Key) };
            foreach (var m in group) item.Items.Add(Shortcut(m));
            menu.Items.Add(item);
        }
    }

    private async void OnDefineMyMaps(object? sender, RoutedEventArgs e)
    {
        var maps = new MyMapsViewModel(Vm.MyMapsToEdit());
        if (await new MyMapsWindow { DataContext = maps }.ShowDialog<bool>(this)) Vm.SaveMyMaps(maps.Maps);
    }

    protected void OnAddToMyMaps(object? sender, RoutedEventArgs e)
    {
        if (Vm.SelectedSymbol is { } sh) Vm.AddToMyMaps(sh);
    }
}
