using System.Threading.Tasks;
using Avalonia.Interactivity;
using SuiteApp.Services;
using SuiteApp.ViewModels;
using T7App.ViewModels;
using T7App.Views;

namespace TxSuite.Views;

/// <summary>T7Suite's own actions (its ribbon's pages, groups and captions), for T7 binaries.</summary>
public partial class MainView
{
    private async Task T7FirmwareInformation()
    {
        if (Vm.T7.FirmwareInfo() is not { } info) return;
        var dialog = new FirmwareInfoWindow { DataContext = info };
        info.Hint += text => _ = Dialogs.Info(dialog, text, Vm.Caption);
        if (await dialog.ShowDialog<bool>(Host))
            Vm.T7.ApplyFirmware(info, text => Dialogs.Wait(() => Dialogs.YesNo(Host, text, "Question")));
    }

    // Realtime → Extra functions (frmMain.SpawnSaabOpenTech): the switches and the SID test first say the I-bus is needed (T7Suite's
    // "Lawicel only:" is gone, any adapter that opens the I-bus does), then the report
    private async void OnSaabOpenTech(object? sender, RoutedEventArgs e)
    {
        if ((sender as Avalonia.Controls.MenuItem)?.Tag is not string arguments) return;
        if (arguments[0] is 'S' or 'U' or 'T') await Dialogs.Info(Host, "Please note that you will have to be connected to the cars I-bus for this to work!");
        if (await Vm.T7.SaabOpenTechAsync(arguments) is { } report) await Dialogs.Text(Host, "Communication report", report);
    }

    private async void OnSyncToBinary(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.OkCancel(Host, "This will overwrite data in your binary file. Are you sure you want to proceed?", "Warning!")) await Vm.T7.SyncToBinaryAsync();
    }

    private async void OnSyncToEcu(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.OkCancel(Host, "This will overwrite data in your ECU. Are you sure you want to proceed?", "Warning!")) await Vm.T7.SyncToEcuAsync();
    }

    private async void OnUploadPackage(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.OpenFile(Host, "Upload tuning package to ECU", "Trionic 7 packages", ["*.t7p"]) is { } file) await Vm.T7.UploadPackageAsync(file);
    }

    private async void OnGeneratePackage(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.SaveFile(Host, "Generate tuning package from ECU", "Trionic 7 packages", "t7p", null) is { } file) await Vm.T7.GeneratePackageAsync(file);
    }

    private async void OnReadSymbolFromEcu(object? sender, RoutedEventArgs e)
    {
        if (Vm.SelectedSymbol is not { } sh || Vm.Binary is not global::T7.T7Binary) return;
        Vm.OpenSymbolByName(sh.SmartVarname);
        if (Vm.SelectedViewer is MapViewerViewModel { CanSaveToFile: true } viewer && viewer.MapName == sh.SmartVarname)
            await Vm.ReadMapFromEcuAsync(viewer);
    }

    private async void OnSidInfo(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is not global::T7.T7Binary bin) return;
        if (global::T7.SidInfo.Read(bin) is not { } rows)
        {
            await Dialogs.Info(Host, "File not compatible!");
            return;
        }
        var sid = new SidInfoViewModel(bin, rows);
        if (!await new SidInfoWindow { DataContext = sid }.ShowDialog<bool>(Host)) return;
        try
        {
            sid.Save(Vm.Settings.AutoFixFooter);
        }
        catch (System.Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException or System.InvalidOperationException)
        {
            await Dialogs.Info(Host, "Failed to write to binary. Is it read-only? Details: " + ex.Message);
        }
    }

    private async void OnEsp(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is not global::T7.T7Binary bin) return;
        if (global::T7.FirmwareTools.ReadEsp(bin) is not { } current)
        {
            await Dialogs.Info(Host, "File not compatible!");
            return;
        }
        var esp = new EspViewModel(current);
        if (await new EspWindow { DataContext = esp }.ShowDialog<bool>(Host) && esp.Value is { } value)
            await WriteSafely(() => global::T7.FirmwareTools.WriteEsp(bin, value, Vm.Settings.AutoFixFooter));
    }

    private async void OnTcm(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is not global::T7.T7Binary bin) return;
        if (bin.FindAny("VIOSCal.M_TCMOffset") == null)
        {
            await Dialogs.Info(Host, "File not compatible, symbol VIOSCal.M_TCMOffset missing!");
            return;
        }
        if (global::T7.FirmwareTools.ReadTcm(bin) is not { } before)
        {
            await Dialogs.Info(Host, "File not compatible!");
            return;
        }
        var tcm = new TcmViewModel(before);
        if (!await new TcmWindow { DataContext = tcm }.ShowDialog<bool>(Host)) return;
        int count = Vm.TransactionLog?.TransCollection.Count ?? 0;
        await WriteSafely(() => global::T7.FirmwareTools.WriteTcm(bin, before, tcm.After, Vm.Settings.AutoFixFooter, Vm.TransactionLog));
        Vm.TransactionsAdded(count);
        Vm.RefreshViewers(bin.FileName);
    }
}
