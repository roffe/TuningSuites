using System.Linq;
using System.Threading.Tasks;
using Avalonia.Interactivity;
using SuiteApp.Services;
using T5App.ViewModels;
using T5App.Views;

namespace TxSuite.Views;

/// <summary>T5Suite 2.0's own actions (its ribbon's pages, groups and captions), for T5 binaries.</summary>
public partial class MainView
{
    private T5Module T5 => Vm.T5;

    // T5Suite's data transfer wizard
    private static readonly (string, string) T5TransferWizard =
        ("Data transfer wizard", "Welcome to the data transfer wizard\n\nThis wizard will help you transferring data from the currect binary to the target binary file.\n\n"
            + "Make sure the source and target binary files are for the same engine type and such to prevent problem in ignition advance and fuelling from occuring!\n\n"
            + "Please review the target binary for correctness after the wizard finishes.");

    // an action picked in the partnumber's lookup closes the dialog unchanged first (frmEasyFirmwareSettings' Abort)
    private async Task<bool> ShowFirmwareOptions(FirmwareOptionsViewModel options)
    {
        var dialog = new FirmwareOptionsWindow { DataContext = options };
        (string action, string stock)? pick = null;
        dialog.LookupPartnumber = async () =>
        {
            pick = await ShowPartLookupAsync(dialog, options.Partnumber.Trim());
            if (pick != null) dialog.Close(false);
        };
        bool ok = await dialog.ShowDialog<bool>(Host);
        if (pick is { } p) await RunPartLookupAsync(p.action, p.stock);
        return ok;
    }

    // ---- Tuning wizards ----

    private async void OnTuneMeUp(object? sender, RoutedEventArgs e)
    {
        if (T5.TuneMeUp() is not { } wizard || !await new TuneMeUpWindow { DataContext = wizard }.ShowDialog<bool>(Host)) return;
        if (await T5.RunTuneMeUpAsync(wizard) is { } report) await Dialogs.Report(Host, $"Tuning report (stage {wizard.Stage})", report);
    }

    private async void OnMapSensor(object? sender, RoutedEventArgs e)
    {
        if ((sender as Avalonia.Controls.Control)?.Tag is not string tag || !System.Enum.TryParse(tag, out Trionic5Tools.MapSensorType to)) return;
        string from = Trionic5Tools.T5Tuning.SensorName(T5.CurrentMapSensor), target = Trionic5Tools.T5Tuning.SensorName(to);
        if (!await Dialogs.YesNo(Host, "All boost related tables will be altered to make sure the correct values are calculated within the ECU based on the new mapsensor type.\n\n" +
                $"You are converting from a {from} to a {target}.\n\n" +
                "If you used the wizard on a file that was already converted for use with a non-stock mapsensor, be sure to verify the binary file after the wizard completes.",
                "Map sensor wizard")) return;
        await Dialogs.Report(Host, target + " report", await T5.ConvertMapSensorAsync(to));
    }

    private async void OnInjectors(object? sender, RoutedEventArgs e)
    {
        if (T5.InjectorWizard() is { } wizard && await new InjectorWizardWindow { DataContext = wizard }.ShowDialog<bool>(Host)) await T5.ApplyInjectorsAsync(wizard);
    }

    private async void OnE85(object? sender, RoutedEventArgs e)
    {
        if (!await Dialogs.YesNo(Host, "Please note that Trionic 5 does not support bi-fuelling. Once your binary has been converted to E85 it will no longer run on regular petrol. " +
                "The wizard will alter ignition settings, cranking and afterstart fuelling and normal fuelling.\n\n" +
                "The coldstart and warmup factors are estimated, check them with a wideband lambda sensor.", "E85 wizard")) return;
        await Dialogs.Report(Host, "Convert to E85 report", await T5.ConvertToE85Async());
    }

    private async void OnBoostAdaption(object? sender, RoutedEventArgs e)
    {
        if (T5.BoostAdaption() is not { } b) return;
        if (await Dialogs.Numbers(Host, "Boost adaption range wizard",
                "Please note that this wizard will adjust the actual code in your binary! The boost adaption range should match the spool characteristics of your turbo.",
                ("Manual gearbox from (rpm)", b.ManualLow, 1000, 9000, 50), ("Manual gearbox upto (rpm)", b.ManualHigh, 1000, 9000, 50),
                ("Automatic gearbox from (rpm)", b.AutomaticLow, 1000, 9000, 50), ("Automatic gearbox upto (rpm)", b.AutomaticHigh, 1000, 9000, 50),
                ("Max. boost error (bar)", b.BoostError / 100m, 0.01m, 0.2m, 0.01m)) is not { } v) return;
        await T5.SetBoostAdaptionAsync(new((int)v[0], (int)v[1], (int)v[2], (int)v[3], (int)System.Math.Round(v[4] * 100)));
    }

    private async void OnBoostBias(object? sender, RoutedEventArgs e)
    {
        if (T5.BoostBiasStep() is not { } step)
        {
            T5.ShowInfo("This file has no 16-bit boost bias map, its range can't be changed");
            return;
        }
        // frmBoostBiasWizard recalculated the range on every step: 2500 + step × 30 × 10
        if (await Dialogs.Numbers(Host, "Boost bias RPM range wizard", "Please note that this wizard will adjust the actual code in your binary!",
                s => $"Result rpm from 2500 rpm upto {2500 + (int)s[0] * 300}", ("Axis step range", step, 5, 20, 1)) is { } v)
            await T5.SetBoostBiasStepAsync((int)v[0]);
    }

    private async void OnRpmLimit(object? sender, RoutedEventArgs e)
    {
        if (T5.RpmLimits() is not { } r) return;
        if (await Dialogs.Numbers(Host, "RPM limiter wizard", "Welcome to the RPM limiter wizard",
                ("Software RPM limit", r.software, 5000, 10000, 100), ("Hardcoded RPM limit", r.hardcoded, 5000, 10000, 100)) is { } v)
            await T5.SetRpmLimitsAsync((int)v[1], (int)v[0]);
    }

    // ---- Actions page ----

    private async void OnExamine(object? sender, RoutedEventArgs e) => await Dialogs.Report(Host, "Examination report", T5.Examine());

    private async void OnAnomalies(object? sender, RoutedEventArgs e) => await Dialogs.Report(Host, "Anomaly report", T5.Anomalies());

    /// <summary>Open a saved report: T5Suite read DevExpress .prnx files, the reports here are saved as text.</summary>
    private async void OnOpenReport(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.OpenFile(Host, "Open a saved report", "Reports", ["*.txt"]) is { } file)
            await Dialogs.Report(Host, System.IO.Path.GetFileNameWithoutExtension(file), await System.IO.File.ReadAllLinesAsync(file));
    }

    private async void OnMergeFiles(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.OpenFile(Host, "First part of binary", "Binary files", ["*.bin"]) is not { } first
            || await Dialogs.OpenFileIn(Host, "Second part of binary", System.IO.Path.GetDirectoryName(first), "Binary files", ["*.bin"]) is not { } second
            || await Dialogs.SaveFile(Host, "Save merged binary to...", "Binary files", "bin", "merged.bin") is not { } output) return;
        if (Trionic5Tools.T5Binary.Merge(await System.IO.File.ReadAllBytesAsync(first), await System.IO.File.ReadAllBytesAsync(second)) is not { } merged)
        {
            await Dialogs.Info(Host, "File lengths don't match, unable to merge!");
            return;
        }
        await System.IO.File.WriteAllBytesAsync(output, merged);
        await Dialogs.Info(Host, "Files merged successfully");
    }

    /// <summary>Split binary file: chip1.bin and chip2.bin next to the open file (overwritten, as T5Suite did).</summary>
    private async void OnSplitFile(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is not { } bin) return;
        string dir = System.IO.Path.GetDirectoryName(bin.FileName) ?? "";
        var (chip1, chip2) = Trionic5Tools.T5Binary.Split(await System.IO.File.ReadAllBytesAsync(bin.FileName));
        await System.IO.File.WriteAllBytesAsync(System.IO.Path.Combine(dir, "chip1.bin"), chip1);
        await System.IO.File.WriteAllBytesAsync(System.IO.Path.Combine(dir, "chip2.bin"), chip2);
        await Dialogs.Info(Host, "File split to chip1.bin and chip2.bin");
    }

    // ---- Online tuning: SRAM ----

    private async void OnDownloadSram(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is null) return;
        if (!Vm.IsConnected)
        {
            await Dialogs.Info(Host, "A canbus connection is needed to create a SRAM snapshot");
            return;
        }
        if ((T5.ProjectSnapshotFile() ?? await Dialogs.SaveFile(Host, "Get SRAM snapshot", "SRAM snapshots", "RAM", T5.SnapshotName)) is { } file)
            await T5.DownloadSramAsync(file);
    }

    private async void OnUploadSram(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is null || !Vm.IsConnected || await Dialogs.OpenFile(Host, "Upload SRAM to ECU", "SRAM snapshots", ["*.ram"]) is not { } file) return;
        bool toFile = await Dialogs.YesNo(Host, "Do you want to write to the current binary file as well?", "Question");
        await T5.UploadSramAsync(file, toFile);
    }

    private async void OnCompareEcu(object? sender, RoutedEventArgs e) => await T5.CompareEcuWithBinaryAsync();

    private async void OnKnockSnapshots(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary != null) await T5ToolWindows.KnockSnapshots(Host, T5);
    }

    /// <summary>Autotune ignition: starts or stops it on the running realtime panel.</summary>
    private async void OnIgnitionAutotune(object? sender, RoutedEventArgs e)
    {
        if (Vm.Realtime is T5RealtimeViewModel rt) await rt.ToggleIgnitionAutotuneAsync();
        else await Dialogs.Info(Host, "Start the realtime panel first (Realtime → Toggle realtime panel)", Vm.Caption);
    }

    /// <summary>Import SRAM snapshot into binary: the snapshot, then "Select merge options" (frmMergeAdaptionData).</summary>
    private async void OnMergeAdaption(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is null || await Dialogs.OpenFile(Host, "Select SRAM file...", "SRAM dumps", ["*.ram"]) is not { } file) return;
        if (await Dialogs.Checks(Host, "Select merge options", "Select items to merge from adaption data",
                ("Fuel adaption (spot adaption)", true), ("Long term fuel trim", true), ("Idle fuel trim", true),
                ("Cylinder fuel correction from knock information", false)) is { } o)
            await T5.MergeAdaptionAsync(file, new Trionic5Tools.T5Tuning.AdaptionMerge(o[0], o[1], o[2], o[3]));
    }

    // ---- Actions page: T5Suite's tools ----

    private void OnAddToRealtimeUserMaps(object? sender, RoutedEventArgs e)
    {
        if (Vm.SelectedSymbol is { } sh) T5.AddRealtimeUserMap(sh);
    }

    private async void OnUserLibrary(object? sender, RoutedEventArgs e) => await T5ToolWindows.UserLibrary(Host, T5);

    private void OnDyno(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary != null) T5ToolWindows.Dyno(Host, T5);
    }

    private void OnCompressorMap(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary != null) T5ToolWindows.Compressor(Host, T5);
    }

    private void OnInjectionTiming(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary != null) T5ToolWindows.InjectionTiming(Host, T5);
    }
}
