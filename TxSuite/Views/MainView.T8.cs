using System.Threading.Tasks;
using Avalonia.Interactivity;
using SuiteApp.Services;
using T8App.ViewModels;
using T8App.Views;

namespace TxSuite.Views;

/// <summary>T8Suite's own actions (its ribbon's pages, groups and captions), for T8 binaries.</summary>
public partial class MainView
{
    // txlogger's Firmware info edit: T8Suite's Firmware information (VIN, immobilizer code and software version)
    private async Task T8FirmwareInformation()
    {
        if (Vm.T8.FirmwareInfo() is not { } info) return;
        var firmware = new FirmwareInfoViewModel(info);
        var dialog = new FirmwareInfoWindow { DataContext = firmware };
        firmware.Hint += text => _ = Dialogs.Info(dialog, text, Vm.Caption);
        if (await dialog.ShowDialog<bool>(Host)) await Vm.T8.ApplyFirmwareAsync(firmware.ToEdit());
    }

    private async void OnRecoverEcu(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.OpenFile(Host, "Recover ECU", "Binary files", ["*.bin"]) is { } file) await Vm.T8.RecoverEcuAsync(file);
    }

    private async void OnEcuInformation(object? sender, RoutedEventArgs e)
    {
        if (await Vm.T8.ReadEcuInfoAsync() is { } rows) new EcuInfoWindow(rows).Show(Host);
    }

    // Actions → TEM editor / PID editor
    private async void OnPidEditor(object? sender, RoutedEventArgs e)
    {
        if (Vm.T8.PidEditor((sender as Avalonia.Controls.Control)?.Tag is "tem") is not { } editor) return;
        if (await new PidEditorWindow { DataContext = editor }.ShowDialog<bool>(Host)) await Vm.T8.ApplyPidEditorAsync(editor);
    }

    // File → Create binary from TIS file: the base bin (from Binaries/T8), the TIS file (T8Suite saved the base's first part when that
    // was cancelled), then where to save it
    private async void OnCreateFromTis(object? sender, RoutedEventArgs e)
    {
        string binaries = CommonSuite.PartInfo.StockFolder("T8");
        if (await Dialogs.OpenFileIn(Host, "Select a binary file to base the new file on", binaries, "Binary files", ["*.bin"]) is not { } baseBin
            || !T8SuitePro.T8Binary.IsValidFile(baseBin)) return;
        string? tis = await Dialogs.OpenFile(Host, "Choose a TIS T8 file to build the new file with", "TIS T8 files", ["*.gbf", "*.s19"]);
        if (await Dialogs.SaveFile(Host, "Choose a filename for the new binary file", "Binary files", "bin", null) is { } target) Vm.T8.CreateFromTis(baseBin, tis, target);
    }

    // Tuning → Tuning Wizard
    private async void OnTuningWizard(object? sender, RoutedEventArgs e)
    {
        if (Vm.T8.TuningWizard() is { } wizard) await new TuningWizardWindow { DataContext = wizard }.ShowDialog(Host);
    }
}
