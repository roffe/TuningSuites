using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using T7App.Services;
using T7App.ViewModels;

namespace T7App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // Enter opens the symbol; the grid would move to the next row, so catch it on the way down, unless a cell is being edited
        SymbolGrid.AddHandler(KeyDownEvent, OnSymbolKeyDown, RoutingStrategies.Tunnel);
        SymbolGrid.BeginningEdit += (_, _) => m_editingSymbol = true;
    }

    private bool m_editingSymbol;

    private MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MainWindowViewModel vm)
        {
            vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MainWindowViewModel.Binary)) BuildQuickMaps(); };
            vm.MyMapsChanged += BuildMyMaps;
            BuildMyMaps();
            vm.Info += text => _ = Dialogs.Info(this, text, "T7Suite");
            vm.AskYesNoCancel = text => Dialogs.YesNoCancel(this, text, "Question");
            vm.AskText = caption => Dialogs.Prompt(this, caption);
            vm.AskOkCancel = text => Dialogs.OkCancel(this, text, "Transaction log size warning...");
        }
    }

    private async void OnOpenFile(object? sender, RoutedEventArgs e)
    {
        string? path = await Dialogs.OpenFile(this, "Trionic 7 binary or Motorola S19", "*.bin", "*.s19");
        if (path != null) await Vm.OpenPlainFileAsync(path, true);
    }

    private async void OnFirmwareInformation(object? sender, RoutedEventArgs e)
    {
        if (Vm.FirmwareInfo() is not { } info) return;
        var dialog = new FirmwareInfoWindow { DataContext = info };
        info.Hint += text => _ = Dialogs.Info(dialog, text, "T7Suite");
        if (await dialog.ShowDialog<bool>(this))
            Vm.ApplyFirmware(info, text => Dialogs.Wait(() => Dialogs.YesNo(this, text, "Question")));
    }

    // ---- ECU ----

    private async void OnReadEcu(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.SaveFile(this, "Binary files", "bin") is { } file) await Vm.ReadEcuAsync(file);
    }

    private async void OnFlashEcu(object? sender, RoutedEventArgs e) =>
        await Vm.FlashEcuAsync(text => Dialogs.YesNo(this, text, "Question"));

    private async void OnFaultCodes(object? sender, RoutedEventArgs e)
    {
        if (await Vm.ReadFaultCodesAsync() is { } codes) new FaultCodesWindow(Vm, codes).Show(this);
    }

    private async void OnSyncToBinary(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.OkCancel(this, "This will overwrite data in your binary file. Are you sure you want to proceed?", "Warning!")) await Vm.SyncToBinaryAsync();
    }

    private async void OnSyncToEcu(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.OkCancel(this, "This will overwrite data in your ECU. Are you sure you want to proceed?", "Warning!")) await Vm.SyncToEcuAsync();
    }

    private async void OnImportSram(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.OpenFile(this, "SRAM dump files", "*.RAM") is { } file) Vm.ImportSramSnapshot(file);
    }

    private async void OnReadSymbolFromEcu(object? sender, RoutedEventArgs e)
    {
        if (Vm.SelectedSymbol is not { } sh || Vm.Binary is not { } bin) return;
        Vm.OpenSymbolByName(sh.SmartVarname);
        if (Vm.SelectedViewer is MapViewerViewModel { CanSaveToFile: true } viewer && viewer.MapName == sh.SmartVarname)
            await Vm.ReadMapFromEcuAsync(viewer);
    }

    private void OnReadFromSramFile(object? sender, RoutedEventArgs e)
    {
        if (Vm.SelectedSymbol is { } sh) Vm.OpenFromSramFile(sh);
    }

    protected override void OnClosed(System.EventArgs e)
    {
        base.OnClosed(e);
        Vm.Ecu.Dispose();
    }

    // ---- map menus ----

    private MenuItem Shortcut(T7.MapShortcut m) =>
        new() { Header = m.Caption, Command = Vm.OpenShortcutCommand, CommandParameter = m, [ToolTip.TipProperty] = m.Symbol };

    // the ribbon's map buttons, grouped as the ribbon was, only the ones this bin has (DynamicTuningMenu)
    private void BuildQuickMaps()
    {
        QuickMapsMenu.Items.Clear();
        if (Vm.Binary is not { } bin) return;
        foreach (var group in T7.MapMenus.QuickMaps(bin).GroupBy(m => m.Group))
        {
            var item = new MenuItem { Header = group.Key };
            foreach (var m in group) item.Items.Add(Shortcut(m));
            QuickMapsMenu.Items.Add(item);
        }
    }

    private void BuildMyMaps()
    {
        MyMapsMenu.Items.Clear();
        var define = new MenuItem { Header = "Define myMaps..." };
        define.Click += OnDefineMyMaps;
        MyMapsMenu.Items.Add(define);
        var maps = T7.MapMenus.LoadMyMaps(Vm.MyMapsFile);
        if (maps.Count > 0) MyMapsMenu.Items.Add(new Separator());
        foreach (var group in maps.GroupBy(m => m.Group))
        {
            var item = new MenuItem { Header = group.Key };
            foreach (var m in group) item.Items.Add(Shortcut(m));
            MyMapsMenu.Items.Add(item);
        }
    }

    private async void OnDefineMyMaps(object? sender, RoutedEventArgs e)
    {
        var maps = new MyMapsViewModel(T7.MapMenus.LoadMyMaps(Vm.MyMapsFile));
        if (await new MyMapsWindow { DataContext = maps }.ShowDialog<bool>(this)) Vm.SaveMyMaps(maps.Maps);
    }

    private void OnAddToMyMaps(object? sender, RoutedEventArgs e)
    {
        if (Vm.SelectedSymbol is { } sh) Vm.AddToMyMaps(sh);
    }

    private async void OnSettings(object? sender, RoutedEventArgs e)
    {
        var settings = new SettingsViewModel(Vm.Settings);
        if (!await new SettingsWindow { DataContext = settings }.ShowDialog<bool>(this)) return;
        settings.Apply(Vm.Settings);
        Vm.SettingsChanged();
    }

    // ---- file actions, imports and exports ----

    private async void OnSaveAs(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is not { } bin || await Dialogs.SaveFile(this, "Binary files", "bin", System.IO.Path.GetFileName(bin.FileName)) is not { } target) return;
        System.IO.File.Copy(bin.FileName, target, true);
        if (await Dialogs.YesNo(this, "Do you want to open the newly saved file?", "Question")) await Vm.OpenPlainFileAsync(target, true);
    }

    private async void OnExportS19(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is { } bin && await Dialogs.SaveFile(this, "S19 files", "S19", System.IO.Path.ChangeExtension(System.IO.Path.GetFileName(bin.FileName), ".S19")) is { } target)
            T7.SymbolFiles.ExportS19(bin, target);
    }

    private async void OnGenerateIdc(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is { } bin) await Dialogs.Info(this, "Generated " + T7.SymbolFiles.ExportIdc(bin), "T7Suite");
    }

    private async void OnImportXml(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.OpenFile(this, "XML documents", "*.xml") is { } file) Vm.ImportSymbols(bin => T7.SymbolFiles.ImportXml(bin, file));
    }

    private async void OnImportCsv(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.OpenFile(this, "CSV documents", "*.csv") is { } file) Vm.ImportSymbols(bin => T7.SymbolFiles.ImportCsv(bin, file));
    }

    private async void OnImportAs2(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.OpenFile(this, "AS2 documents", "*.as2") is { } file) Vm.ImportSymbols(bin => T7.SymbolFiles.ImportAs2(bin, file));
    }

    private async void OnExportMapCsv(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is not { } bin) return;
        if (Vm.SelectedSymbol is not { } sh)
        {
            await Dialogs.Info(this, "No symbol selected in the primary symbol list", "T7Suite");
            return;
        }
        string name = System.IO.Path.GetFileName(bin.FileName) + "~" + sh.SmartVarname + ".csv";
        if (await Dialogs.SaveFile(this, "CSV files", "csv", name) is { } target) T7.SymbolFiles.ExportMapCsv(bin, sh, target);
    }

    private async void OnExportPackage(object? sender, RoutedEventArgs e)
    {
        var selected = SymbolGrid.SelectedItems.OfType<CommonSuite.SymbolHelper>().ToList();
        if (Vm.Binary is { } bin && selected.Count > 0 && await Dialogs.SaveFile(this, "Trionic 7 packages", "t7p") is { } target)
            T7.SymbolFiles.ExportPackage(bin, selected, target);
    }

    private async void OnExportFixedPackage(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is { } bin && await Dialogs.SaveFile(this, "Trionic 7 packages", "t7p") is { } target)
            T7.SymbolFiles.ExportPackage(bin, T7.SymbolFiles.FixedPackage(bin), target);
    }

    private async void OnExportSymbolCsv(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is { } bin && await Dialogs.SaveFile(this, "CSV files", "csv") is { } target)
        {
            T7.SymbolFiles.ExportSymbolCsv(bin, target);
            await Dialogs.Info(this, "Export done", "T7Suite");
        }
    }

    private async void OnSearchMaps(object? sender, RoutedEventArgs e)
    {
        var options = new SearchMapsViewModel();
        if (Vm.Binary is { } bin && await new SearchMapsWindow { DataContext = options }.ShowDialog<bool>(this)) Vm.SearchMaps(options.ToOptions());
    }

    // ---- compare ----

    private async void OnCompareToFile(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.OpenFile(this, "binary files", "*.bin") is { } file) await Vm.CompareToFileAsync(file);
    }

    private async void OnBinaryCompare(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is not { } bin || await Dialogs.OpenFile(this, "binary files", "*.bin") is not { } file) return;
        var lines = T7.T7Compare.BinaryDiff(bin.FileName, file);
        var text = new System.Text.StringBuilder($"{System.IO.Path.GetFileName(bin.FileName)} / {System.IO.Path.GetFileName(file)}: {lines.Count} lines differ\n\n");
        foreach (var (mine, theirs) in lines) text.Append(mine).Append('\n').Append(theirs).Append("\n\n");
        await Dialogs.Text(this, "Binary compare", text.ToString());
    }

    private async void OnTransferMaps(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is not { } bin) return;
        const string text = "This wizard assists you in transferring map contents from the current file to another binary.\n\n"
            + "Make sure engine types and such are equal for both binaries!\n\n"
            + "The author does not take responsibility for any damage done to your car or other objects in any form!\n\n"
            + "Select the target binary now?";
        if (!await Dialogs.YesNo(this, text, "Transfer maps to different binary wizard")) return;
        if (await Dialogs.OpenFile(this, "binary files", "*.bin") is not { } target) return;
        var selection = new TransferSelectionViewModel(T7.T7Compare.TransferCandidates(bin), Vm.LastTransferSelection());
        if (!await new TransferSelectionWindow { DataContext = selection }.ShowDialog<bool>(this)) return;
        var report = Vm.TransferMaps(target, selection.Selection);
        await Dialogs.Text(this, "Data transfer report", string.Join('\n', report));
    }

    // ---- projects ----

    private async void OnCreateProject(object? sender, RoutedEventArgs e)
    {
        ProjectPropertiesViewModel p = Vm.NewProjectProperties();
        if (await new ProjectPropertiesWindow { DataContext = p }.ShowDialog<bool>(this)) await Vm.CreateProjectAsync(p);
    }

    private async void OnOpenProject(object? sender, RoutedEventArgs e)
    {
        var projects = T7.T7Project.List(Vm.Settings.ProjectFolder);
        if (projects.Count == 0)
        {
            await Dialogs.Info(this, "No projects were found, please create one first!", "T7Suite");
            return;
        }
        if (await new ProjectSelectionWindow(projects).ShowDialog<string?>(this) is { Length: > 0 } name) await Vm.OpenProjectAsync(name);
    }

    private async void OnEditProject(object? sender, RoutedEventArgs e)
    {
        if (Vm.Project is not { } project) return;
        var p = ProjectPropertiesViewModel.From(project.Properties);
        if (await new ProjectPropertiesWindow { DataContext = p }.ShowDialog<bool>(this)) await Vm.EditProjectAsync(p);
    }

    private void OnShowTransactionLog(object? sender, RoutedEventArgs e) =>
        new TransactionLogWindow { DataContext = new TransactionLogViewModel(Vm) }.Show(this);

    private void OnShowLogbook(object? sender, RoutedEventArgs e)
    {
        if (Vm.Project is { } project) new LogbookWindow { DataContext = new LogbookViewModel(project) }.Show(this);
    }

    private async void OnRebuild(object? sender, RoutedEventArgs e)
    {
        var p = new RebuildViewModel();
        if (!await new RebuildWindow { DataContext = p }.ShowDialog<bool>(this)) return;
        // the date picker gives midnight; the whole chosen day counts
        System.DateTime upTo = (p.UpTo ?? System.DateTime.Now).Date.AddDays(1).AddTicks(-1);
        if (Vm.Rebuild(upTo, p.StoreAsCurrent) is not { } rebuilt) return;
        if (await Dialogs.SaveFile(this, "Binary files", "bin", "rebuild.bin") is { } dest) System.IO.File.Copy(rebuilt, dest, true);
        System.IO.File.Delete(rebuilt);
    }

    private async void OnProduceLatest(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is { } bin && await Dialogs.SaveFile(this, "Binary files", "bin", System.IO.Path.GetFileName(bin.FileName)) is { } dest)
            System.IO.File.Copy(bin.FileName, dest, true);
    }

    // unsaved maps ask before the app closes; Cancel keeps it open
    private bool m_closeConfirmed;

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (DataContext is MainWindowViewModel { Ecu.IsFlashing: true } busy)
        {
            // stopping a flash halfway leaves the ECU without a working program
            e.Cancel = true;
            busy.ShowInfo("Wait until the ECU operation has finished before closing T7Suite.");
            return;
        }
        if (m_closeConfirmed || DataContext is not MainWindowViewModel vm || !vm.Viewers.OfType<MapViewerViewModel>().Any(v => v.Map.Mutated)) return;
        e.Cancel = true;
        if (!await vm.CloseMutatedViewersAsync()) return;
        m_closeConfirmed = true;
        Close();
    }

    private void OnSymbolCellEditEnded(object? sender, DataGridCellEditEndedEventArgs e)
    {
        m_editingSymbol = false;
        if (e.EditAction == DataGridEditAction.Commit) Vm.SaveUserDescriptions();
    }

    private void OnSymbolDoubleTapped(object? sender, TappedEventArgs e) => Vm.OpenSymbolCommand.Execute(Vm.SelectedSymbol);

    private void OnSymbolKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || m_editingSymbol) return;
        Vm.OpenSymbolCommand.Execute(Vm.SelectedSymbol);
        e.Handled = true;
    }
}
