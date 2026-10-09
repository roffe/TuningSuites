using Avalonia;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SuiteApp.Services;
using T7App.ViewModels;

namespace T7App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        InitWorkspace();
        using var settings = CommonSuite.SettingsKey.Open(MainWindowViewModel.Suite);
        ApplySkin(settings.GetValue(SkinKey) as string);
        // the symbol list's width from the last session, as T7Suite's saved dock layout kept it
        if (double.TryParse(settings.GetValue(SymbolListKey) as string, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out double width)
            && width is > 0.05 and < 0.95)
            SymbolPane.Proportion = width;
    }

    private const string SymbolListKey = "SymbolListProportion";

    private MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MainWindowViewModel vm)
        {
            Documents.ItemsSource = vm.DockedViewers;
            vm.Viewers.CollectionChanged += (_, e) =>
            {
                foreach (DocumentViewModel d in e.NewItems?.OfType<DocumentViewModel>() ?? []) d.PropertyChanged += OnFloatingChanged;
                foreach (DocumentViewModel d in e.OldItems?.OfType<DocumentViewModel>() ?? [])
                {
                    d.PropertyChanged -= OnFloatingChanged;
                    if (m_floating.Remove(d, out var w)) w.CloseQuietly();
                }
            };
            vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MainWindowViewModel.SelectedViewer)) ActivateDocument(vm.SelectedViewer); };
            ApplyHideSymbolTable();
            vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MainWindowViewModel.Binary)) BuildQuickMaps(); };
            vm.MyMapsChanged += BuildMyMaps;
            BuildMyMaps();
            vm.Info += text => _ = Dialogs.Info(this, text, "T7Suite");
            vm.AskYesNoCancel = text => Dialogs.YesNoCancel(this, text, "Question");
            vm.AcceptAutotune = percent =>
            {
                string map = string.IsNullOrEmpty(vm.Settings.AutoTuneFuelMap) ? "BFuelCal.Map" : vm.Settings.AutoTuneFuelMap;
                double[] x = vm.Binary?.GetXaxisValues("BFuelCal.Map").Select(v => (double)v).ToArray() ?? [];
                double[] y = vm.Binary?.GetYaxisValues("BFuelCal.Map").Select(v => (double)v).ToArray() ?? [];
                return new AutotuneAcceptWindow(map, percent, x, y).ShowDialog<System.Collections.Generic.IReadOnlyCollection<int>?>(this);
            };
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

    private async void OnUploadPackage(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.OpenFile(this, "Trionic 7 packages", "*.t7p") is { } file) await Vm.UploadPackageAsync(file);
    }

    private async void OnGeneratePackage(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.SaveFile(this, "Trionic 7 packages", "t7p") is { } file) await Vm.GeneratePackageAsync(file);
    }

    private async void OnCompareToSram(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.OpenFile(this, "SRAM dumps", "*.ram") is { } file) await Vm.CompareToSramAsync(file);
    }

    private async void OnCompareSram(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.OpenFile(this, "First SRAM dump", "*.ram") is { } first && await Dialogs.OpenFile(this, "Second SRAM dump", "*.ram") is { } second)
            await Vm.CompareSramAsync(first, second);
    }

    private async void OnImportSram(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.OpenFile(this, "SRAM dump files", "*.RAM") is { } file) Vm.ImportSramSnapshot(file);
    }

    internal async void OnReadSymbolFromEcu(object? sender, RoutedEventArgs e)
    {
        if (Vm.SelectedSymbol is not { } sh || Vm.Binary is not { } bin) return;
        Vm.OpenSymbolByName(sh.SmartVarname);
        if (Vm.SelectedViewer is MapViewerViewModel { CanSaveToFile: true } viewer && viewer.MapName == sh.SmartVarname)
            await Vm.ReadMapFromEcuAsync(viewer);
    }

    private async void OnImportTuningPackage(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.OpenFile(this, "Trionic 7 packages", "*.t7p") is { } file && Vm.ImportTuningPackage(file) is { } results)
            await new ImportResultsWindow(results).ShowDialog(this);
    }

    private async void OnSidInfo(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is not { } bin) return;
        if (T7.SidInfo.Read(bin) is not { } rows)
        {
            await Dialogs.Info(this, "File not compatible!");
            return;
        }
        var sid = new SidInfoViewModel(bin, rows);
        if (!await new SidInfoWindow { DataContext = sid }.ShowDialog<bool>(this)) return;
        try
        {
            sid.Save(Vm.Settings.AutoFixFooter);
        }
        catch (System.Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException or System.InvalidOperationException)
        {
            await Dialogs.Info(this, "Failed to write to binary. Is it read-only? Details: " + ex.Message);
        }
    }

    private async void OnEsp(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is not { } bin) return;
        if (T7.FirmwareTools.ReadEsp(bin) is not { } current)
        {
            await Dialogs.Info(this, "File not compatible!");
            return;
        }
        var esp = new EspViewModel(current);
        if (await new EspWindow { DataContext = esp }.ShowDialog<bool>(this) && esp.Value is { } value)
            await WriteSafely(() => T7.FirmwareTools.WriteEsp(bin, value, Vm.Settings.AutoFixFooter));
    }

    private async void OnTcm(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is not { } bin) return;
        if (bin.FindAny("VIOSCal.M_TCMOffset") == null)
        {
            await Dialogs.Info(this, "File not compatible, symbol VIOSCal.M_TCMOffset missing!");
            return;
        }
        if (T7.FirmwareTools.ReadTcm(bin) is not { } before)
        {
            await Dialogs.Info(this, "File not compatible!");
            return;
        }
        var tcm = new TcmViewModel(before);
        if (!await new TcmWindow { DataContext = tcm }.ShowDialog<bool>(this)) return;
        int count = Vm.TransactionLog?.TransCollection.Count ?? 0;
        await WriteSafely(() => T7.FirmwareTools.WriteTcm(bin, before, tcm.After, Vm.Settings.AutoFixFooter, Vm.TransactionLog));
        Vm.TransactionsAdded(count);
        Vm.RefreshViewers(bin.FileName);
    }

    private async System.Threading.Tasks.Task WriteSafely(System.Action write)
    {
        try
        {
            write();
        }
        catch (System.Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException or System.InvalidOperationException)
        {
            await Dialogs.Info(this, "Failed to write to binary. Is it read-only? Details: " + ex.Message);
        }
    }

    private async void OnCopyAddressTable(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is not { } bin || await Dialogs.OpenFile(this, "T7 binary files", "*.bin") is not { } target) return;
        int from = T7.BinaryTools.AddressTableOffset(System.IO.File.ReadAllBytes(bin.FileName));
        int to = T7.BinaryTools.AddressTableOffset(System.IO.File.ReadAllBytes(target));
        if (from != to && !await Dialogs.YesNo(this, "Address table start addresses are not equal, continue anyway?", "Attention!"))
        {
            await Dialogs.Info(this, "Transfer cancelled");
            return;
        }
        try
        {
            T7.BinaryTools.CopyAddressTable(bin.FileName, target, Vm.Settings.AutoFixFooter);
            await Dialogs.Info(this, "Transfer done");
        }
        catch (System.Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException or System.InvalidOperationException)
        {
            await Dialogs.Info(this, ex.Message);
        }
    }

    private async void OnLookupPartnumber(object? sender, RoutedEventArgs e)
    {
        var lookup = new PartLookupViewModel();
        string? action = await new PartLookupWindow { DataContext = lookup }.ShowDialog<string?>(this);
        if (action == null || lookup.Info?.Binary is not { } stock) return;
        if (action == "open") await Vm.OpenFileAsync(stock, true);
        else if (action == "compare") await Vm.CompareToFileAsync(stock);
        else if (action.StartsWith("create:"))
        {
            string file = action[7..];
            System.IO.File.Copy(stock, file, true);
            await Vm.OpenFileAsync(file, true);
        }
    }

    private async void OnVectors(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is { } bin) await new VectorsWindow(T7.Disassembly.Vectors(bin)).ShowDialog(this);
    }

    private async void OnDisassembly(object? sender, RoutedEventArgs e) =>
        await Vm.ShowDisassemblyAsync(false, text => Dialogs.YesNo(this, text, "Question"));

    // the full sweep is reused when it exists, as T7Suite did
    private async void OnFullDisassembly(object? sender, RoutedEventArgs e) =>
        await Vm.ShowDisassemblyAsync(true, _ => System.Threading.Tasks.Task.FromResult(false));

    // ---- skin and help ----

    private const string SkinKey = "Skin";

    /// <summary>Skin: light, dark or the system's theme, remembered (T7Suite's DevExpress skins).</summary>
    private void OnSkin(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string skin }) return;
        ApplySkin(skin);
        using var settings = CommonSuite.SettingsKey.Open(MainWindowViewModel.Suite);
        settings.SetValue(SkinKey, skin);
    }

    private static void ApplySkin(string? skin)
    {
        if (Application.Current is not { } app) return;
        app.RequestedThemeVariant = skin switch
        {
            "Light" => Avalonia.Styling.ThemeVariant.Light,
            "Dark" => Avalonia.Styling.ThemeVariant.Dark,
            _ => Avalonia.Styling.ThemeVariant.Default,
        };
    }

    /// <summary>Help: the manuals next to the program, opened with the system's viewer.</summary>
    private async void OnHelpFile(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string name }) return;
        string file = System.IO.Path.Combine(System.AppContext.BaseDirectory, name);
        try
        {
            if (!System.IO.File.Exists(file)) throw new System.IO.FileNotFoundException();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = true });
        }
        catch (System.Exception)
        {
            await Dialogs.Info(this, $"{name} could not be found or opened!");
        }
    }

    // ---- updates ----

    /// <summary>
    /// frmMain_Shown and Help → Check for updates: the dialog when a newer T7Suite release is out; OK downloads its setup in the
    /// browser on Windows, elsewhere the release page lists the packages. A build without a T7suite_v tag (0.0.0) skips the
    /// startup check, every release would be newer.
    /// </summary>
    public async System.Threading.Tasks.Task CheckForUpdatesAsync(bool startup)
    {
        if (startup && MainWindowViewModel.BuildVersion == new System.Version(0, 0, 0, 0)) return;
        if (await Vm.CheckForUpdatesAsync() is not { } release) return;
        if (await new UpdateAvailableWindow(release).ShowDialog<bool>(this))
            Dialogs.OpenWithShell(System.OperatingSystem.IsWindows() && release.Msi != null ? release.Msi : release.Page);
    }

    private async void OnCheckForUpdates(object? sender, RoutedEventArgs e) => await CheckForUpdatesAsync(false);

    // T7Suite's release notes viewer showed the updater's notes; they're the GitHub releases' now
    private void OnReleaseNotes(object? sender, RoutedEventArgs e) => Dialogs.OpenWithShell(CommonSuite.UpdateCheck.ReleasesPage);

    private async void OnAbout(object? sender, RoutedEventArgs e)
    {
        string version = System.Reflection.Assembly.GetEntryAssembly()?
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "";
        // the build metadata (+commit) doesn't belong in the title
        int plus = version.IndexOf('+');
        await new AboutWindow(plus > 0 ? version[..plus] : version).ShowDialog(this);
    }

    // ---- logs ----

    private async void OnOpenLog(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.OpenFile(this, "Trionic 7 logfiles", "*.t7l") is not { } file) return;
        await Vm.OpenLogAsync(file, sections => new ChoiceWindow("Select logfile section to display", sections).ShowDialog<int?>(this));
    }

    private async System.Threading.Tasks.Task<(string file, LogSelectionViewModel selection)?> ChooseLogData()
    {
        if (await Dialogs.OpenFile(this, "Trionic 7 logfiles", "*.t7l") is not { } file) return null;
        LogSelectionViewModel selection = Vm.LogSelection(file);
        return await new LogSelectionWindow { DataContext = selection }.ShowDialog<bool>(this) ? (file, selection) : null;
    }

    private async void OnExportLogCsv(object? sender, RoutedEventArgs e)
    {
        if (await ChooseLogData() is var (file, selection)) Vm.ExportLogCsv(file, selection);
    }

    private async void OnExportLogDif(object? sender, RoutedEventArgs e)
    {
        if (await ChooseLogData() is var (file, selection)) Vm.ExportLogDif(file, selection);
    }

    private async void OnLogMatrix(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.OpenFile(this, "Trionic 7 logfiles", "*.t7l") is not { } file) return;
        var (lines, selection) = Vm.MatrixSelection(file);
        if (await new MatrixSelectionWindow { DataContext = selection }.ShowDialog<bool>(this)) Vm.ShowMatrix(lines, selection);
    }

    private async void OnLogFilters(object? sender, RoutedEventArgs e)
    {
        var filters = new LogFiltersViewModel(Vm.LoadLogFilters(), Vm.LogFilterSymbols);
        if (await new LogFiltersWindow { DataContext = filters }.ShowDialog<bool>(this)) Vm.SaveLogFilters(filters.ToCollection());
    }

    private async void OnSymbolColors(object? sender, RoutedEventArgs e)
    {
        SymbolColorsViewModel colors = Vm.SymbolColorChoices();
        if (await new SymbolColorsWindow { DataContext = colors }.ShowDialog<bool>(this)) colors.Save();
    }

    internal void OnAddToRealtime(object? sender, RoutedEventArgs e)
    {
        if (Vm.SelectedSymbol is { } sh) Vm.AddToRealtime(sh);
    }

    internal void OnReadFromSramFile(object? sender, RoutedEventArgs e)
    {
        if (Vm.SelectedSymbol is { } sh) Vm.OpenFromSramFile(sh);
    }

    protected override void OnClosed(System.EventArgs e)
    {
        base.OnClosed(e);
        if (!double.IsNaN(SymbolPane.Proportion))
            using (var settings = CommonSuite.SettingsKey.Open(MainWindowViewModel.Suite)) settings.SetValue(SymbolListKey, SymbolPane.Proportion);
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

    internal void OnAddToMyMaps(object? sender, RoutedEventArgs e)
    {
        if (Vm.SelectedSymbol is { } sh) Vm.AddToMyMaps(sh);
    }

    private async void OnSettings(object? sender, RoutedEventArgs e)
    {
        var settings = new SettingsViewModel(Vm.Settings);
        if (!await new SettingsWindow { DataContext = settings }.ShowDialog<bool>(this)) return;
        settings.Apply(Vm.Settings);
        Vm.SettingsChanged();
        ApplyHideSymbolTable();
        Logging.ApplyCanLogging(Vm.Settings.EnableCanLog);
    }

    /// <summary>
    /// Hide symbol window: the symbol list pinned to the side (T7Suite's auto hide), sliding out when its tab is pointed at;
    /// opening a map slides it back in (ActivateDocument).
    /// </summary>
    private void ApplyHideSymbolTable()
    {
        if (DockFactory!.IsDockablePinned(SymbolTool) == Vm.Settings.HideSymbolTable) return;
        if (Vm.Settings.HideSymbolTable) DockFactory.PinDockable(SymbolTool);
        else DockFactory.UnpinDockable(SymbolTool);
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

    internal async void OnExportPackage(object? sender, RoutedEventArgs e)
    {
        var selected = Vm.SelectedSymbols.ToList();
        if (Vm.Binary is { } bin && selected.Count > 0 && await Dialogs.SaveFile(this, "Trionic 7 packages", "t7p") is { } target)
            T7.SymbolFiles.ExportPackage(bin, selected, target);
    }

    internal async void OnExportFixedPackage(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is { } bin && await Dialogs.SaveFile(this, "Trionic 7 packages", "t7p") is { } target)
            T7.SymbolFiles.ExportPackage(bin, T7.SymbolFiles.FixedPackage(bin), target);
    }

    internal async void OnExportSymbolCsv(object? sender, RoutedEventArgs e)
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
        var projects = CommonSuite.SuiteProject.List(Vm.Settings.ProjectFolder);
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

    // ---- workspace ----

    private bool m_syncingDock;
    private readonly System.Collections.Generic.HashSet<Dock.Model.Core.IDockable> m_sized = [];

    private Dock.Model.Core.IFactory? DockFactory => Workspace.Factory;

    private void InitWorkspace()
    {
        // the close button goes through the view model, which asks about unsaved maps and then drops the document
        DockFactory!.DockableClosing += (_, e) =>
        {
            if (e.Dockable?.Context is not DocumentViewModel doc) return;
            e.Cancel = true;
            _ = Vm.CloseViewerAsync(doc);
        };
        DockFactory!.ActiveDockableChanged += (_, e) =>
        {
            if (!m_syncingDock && e.Dockable?.Context is DocumentViewModel doc) Vm.SelectedViewer = doc;
            // Dock brings an inner window forward by raising its ZIndex, and Avalonia's compositor re-sorts the windows without
            // repainting them: only the restyled title bars were redrawn, the other window's table and graph stayed painted over
            // the one brought forward until it was dragged. Redrawing each window's frame repaints the whole window.
            foreach (var w in Workspace.GetVisualDescendants().OfType<Dock.Avalonia.Controls.MdiDocumentWindow>())
                w.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "PART_OuterBorder")?.InvalidateVisual();
        };
        // a window dragged by its title bar and let go outside the main window floats (T7Suite's floating panels)
        AddHandler(PointerPressedEvent, OnWorkspacePressed, Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
        AddHandler(PointerReleasedEvent, OnWorkspaceReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
    }

    private DocumentViewModel? m_titleDrag;
    private readonly System.Collections.Generic.Dictionary<DocumentViewModel, FloatingDocumentWindow> m_floating = [];
    private Avalonia.PixelPoint m_floatAt;

    private void OnWorkspacePressed(object? sender, PointerPressedEventArgs e)
    {
        m_titleDrag = null;
        if (e.Source is not Avalonia.Visual source) return;
        bool header = Avalonia.VisualTree.VisualExtensions.GetSelfAndVisualAncestors(source).OfType<Control>().Any(c => c.Name == "PART_Header");
        if (header && Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<Dock.Avalonia.Controls.MdiDocumentWindow>(source)?.DataContext is Dock.Model.Core.IDockable { Context: DocumentViewModel doc })
            m_titleDrag = doc;
    }

    private void OnWorkspaceReleased(object? sender, PointerReleasedEventArgs e)
    {
        DocumentViewModel? doc = m_titleDrag;
        m_titleDrag = null;
        Point p = e.GetPosition(this);
        if (doc == null || new Rect(Bounds.Size).Contains(p)) return;
        Float(doc, this.PointToScreen(p));
    }

    /// <summary>A document into a window of its own at a screen position.</summary>
    public void Float(DocumentViewModel doc, Avalonia.PixelPoint at)
    {
        m_floatAt = at;
        doc.IsFloating = true;
    }

    private void OnFloatingChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DocumentViewModel.IsFloating) || sender is not DocumentViewModel doc) return;
        if (doc.IsFloating && !m_floating.ContainsKey(doc))
        {
            var window = new FloatingDocumentWindow(Vm, doc) { WindowStartupLocation = WindowStartupLocation.Manual, Position = m_floatAt };
            m_floating[doc] = window;
            window.Show(this);
        }
        else if (!doc.IsFloating && m_floating.Remove(doc, out var window))
        {
            window.CloseQuietly();
            ActivateDocument(doc);
        }
    }

    // a document shown from the view model comes to the front (the dock's own document appears after the collection change)
    private void ActivateDocument(DocumentViewModel? viewer)
    {
        if (viewer == null) return;
        if (Vm.Settings.HideSymbolTable && Workspace.Layout is Dock.Model.Controls.IRootDock root) DockFactory!.HidePreviewingDockables(root);
        if (m_floating.TryGetValue(viewer, out var floating))
        {
            floating.Activate();
            return;
        }
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (Documents.VisibleDockables?.FirstOrDefault(d => d.Context == viewer) is not { } dockable) return;
            if (m_sized.Add(dockable))
            {
                // documents stay inner windows: dropping them on dock targets turned them into tabs, splits (an empty strip
                // by the symbol list) or floating windows that couldn't be brought back
                dockable.CanFloat = false;
                dockable.CanDrop = false;
                // a new inner window opens at a working size, cascaded below the last one (Dock's default is small)
                if (dockable is Dock.Model.Controls.IMdiDocument mdi)
                {
                    int n = Documents.VisibleDockables.Count - 1;
                    double width = System.Math.Max(640, Workspace.Bounds.Width * 0.6), height = System.Math.Max(460, Workspace.Bounds.Height * 0.8);
                    mdi.MdiBounds = new Dock.Model.Core.DockRect(24 * (n % 8), 24 * (n % 8), width, height);
                }
            }
            m_syncingDock = true;
            try
            {
                DockFactory!.SetActiveDockable(dockable);
                DockFactory!.SetFocusedDockable(Documents, dockable);
            }
            finally
            {
                m_syncingDock = false;
            }
        });
    }

    private void OnCascade(object? sender, RoutedEventArgs e) => Documents.CascadeDocuments?.Execute(null);

    private void OnTileHorizontal(object? sender, RoutedEventArgs e) => Documents.TileDocumentsHorizontal?.Execute(null);

    private void OnTileVertical(object? sender, RoutedEventArgs e) => Documents.TileDocumentsVertical?.Execute(null);
}
