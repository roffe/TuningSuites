using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SuiteApp.Services;
using SuiteApp.ViewModels;
using SuiteApp.Views;
using T7App.ViewModels;

namespace T7App.Views;

/// <summary>T7Suite's main window: its menus (the ribbon's pages, groups and captions) and the actions only T7Suite has.</summary>
public partial class MainWindow : SuiteMainWindow
{
    public MainWindow() => InitializeComponent();

    private new T7MainWindowViewModel Vm => (T7MainWindowViewModel)DataContext!;

    protected override string BinaryFilesName => "Trionic 7 binary or Motorola S19";

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not T7MainWindowViewModel vm) return;
        vm.AcceptAutotune = percent =>
        {
            string map = string.IsNullOrEmpty(vm.Settings.AutoTuneFuelMap) ? "BFuelCal.Map" : vm.Settings.AutoTuneFuelMap;
            double[] x = vm.Binary?.GetXaxisValues("BFuelCal.Map").Select(v => (double)v).ToArray() ?? [];
            double[] y = vm.Binary?.GetYaxisValues("BFuelCal.Map").Select(v => (double)v).ToArray() ?? [];
            return new AutotuneAcceptWindow(map, percent, x, y).ShowDialog<System.Collections.Generic.IReadOnlyCollection<int>?>(this);
        };
    }

    private async void OnFirmwareInformation(object? sender, RoutedEventArgs e)
    {
        if (Vm.FirmwareInfo() is not { } info) return;
        var dialog = new FirmwareInfoWindow { DataContext = info };
        info.Hint += text => _ = Dialogs.Info(dialog, text, Vm.Caption);
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

    private async void OnReadSymbolFromEcu(object? sender, RoutedEventArgs e)
    {
        if (Vm.SelectedSymbol is not { } sh || Vm.Binary is not T7.T7Binary bin) return;
        Vm.OpenSymbolByName(sh.SmartVarname);
        if (Vm.SelectedViewer is MapViewerViewModel { CanSaveToFile: true } viewer && viewer.MapName == sh.SmartVarname)
            await Vm.ReadMapFromEcuAsync(viewer);
    }


    private async void OnSidInfo(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is not T7.T7Binary bin) return;
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
        if (Vm.Binary is not T7.T7Binary bin) return;
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
        if (Vm.Binary is not T7.T7Binary bin) return;
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



    private async void OnVectors(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is T7.T7Binary bin) await new VectorsWindow(T7.Disassembly.Vectors(bin)).ShowDialog(this);
    }

    private async void OnDisassembly(object? sender, RoutedEventArgs e) =>
        await Vm.ShowDisassemblyAsync(false, text => Dialogs.YesNo(this, text, "Question"));

    // the full sweep is reused when it exists, as T7Suite did
    private async void OnFullDisassembly(object? sender, RoutedEventArgs e) =>
        await Vm.ShowDisassemblyAsync(true, _ => System.Threading.Tasks.Task.FromResult(false));

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

    private void OnAddToRealtime(object? sender, RoutedEventArgs e)
    {
        if (Vm.SelectedSymbol is { } sh) Vm.AddToRealtime(sh);
    }

    private void OnReadFromSramFile(object? sender, RoutedEventArgs e)
    {
        if (Vm.SelectedSymbol is { } sh) Vm.OpenFromSramFile(sh);
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

    private void OnBrowseAxes(object? sender, RoutedEventArgs e)
    {
        if (Vm.SelectedSymbol is { } sh) Vm.BrowseAxes(sh.SmartVarname);
    }
}
