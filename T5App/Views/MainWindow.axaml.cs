using Avalonia.Interactivity;
using SuiteApp.Services;
using SuiteApp.ViewModels;
using SuiteApp.Views;
using T5App.ViewModels;

namespace T5App.Views;

/// <summary>T5Suite 2.0's main window: its menus (the ribbon's pages, groups and captions) and the actions only T5Suite has.</summary>
public partial class MainWindow : SuiteMainWindow
{
    public MainWindow() => InitializeComponent();

    private new T5MainWindowViewModel Vm => (T5MainWindowViewModel)DataContext!;

    protected override string BinaryFilesName => "Trionic 5 files";

    protected override string CompareFilesName => "Trionic 5 binary files";

    // T5Suite 2.0's frmAbout
    protected override (string thanks, string support, string closing) AboutTexts =>
        ("Steve Hayes, Hook, MrAze, Sandy_rus, T5_Germany, Seb, Tomili, sourcode, J.K Nilsson, General Failure, Danibjor, Johnc, tomas0student, Janus0070 and...",
         "T5Suite 2.0 was created with the help of lots of people on ecuproject.com.", "Just4pLeisure ;-)");

    private async void OnVinDecoder(object? sender, RoutedEventArgs e) =>
        await new VinDecoderWindow { DataContext = new VinDecoderViewModel("") }.ShowDialog(this);

    private async void OnFirmwareOptions(object? sender, RoutedEventArgs e)
    {
        if (Vm.FirmwareOptions() is not { } options) return;
        if (await new FirmwareOptionsWindow { DataContext = options }.ShowDialog<bool>(this)) await Vm.ApplyFirmwareAsync(options.Properties);
    }

    private async void OnSettings(object? sender, RoutedEventArgs e)
    {
        var settings = new SettingsViewModel(Vm.Settings, Vm.T5Settings);
        if (!await new SettingsWindow { DataContext = settings }.ShowDialog<bool>(this)) return;
        settings.Apply(Vm.Settings, Vm.T5Settings);
        Vm.SettingsChanged();
        Vm.AdvancedModeChanged();
        ApplyHideSymbolTable();
    }

    // ---- Tuning wizards ----

    private async void OnTuneMeUp(object? sender, RoutedEventArgs e)
    {
        if (Vm.TuneMeUp() is not { } wizard || !await new TuneMeUpWindow { DataContext = wizard }.ShowDialog<bool>(this)) return;
        if (await Vm.RunTuneMeUpAsync(wizard) is { } report) await Dialogs.Report(this, $"Tuning report (stage {wizard.Stage})", report);
    }

    private async void OnMapSensor(object? sender, RoutedEventArgs e)
    {
        if ((sender as Avalonia.Controls.Control)?.Tag is not string tag || !System.Enum.TryParse(tag, out Trionic5Tools.MapSensorType to)) return;
        string from = Trionic5Tools.T5Tuning.SensorName(Vm.CurrentMapSensor), target = Trionic5Tools.T5Tuning.SensorName(to);
        if (!await Dialogs.YesNo(this, "All boost related tables will be altered to make sure the correct values are calculated within the ECU based on the new mapsensor type.\n\n" +
                $"You are converting from a {from} to a {target}.\n\n" +
                "If you used the wizard on a file that was already converted for use with a non-stock mapsensor, be sure to verify the binary file after the wizard completes.",
                "Map sensor wizard")) return;
        await Dialogs.Report(this, target + " report", await Vm.ConvertMapSensorAsync(to));
    }

    private async void OnInjectors(object? sender, RoutedEventArgs e)
    {
        if (Vm.InjectorWizard() is { } wizard && await new InjectorWizardWindow { DataContext = wizard }.ShowDialog<bool>(this)) await Vm.ApplyInjectorsAsync(wizard);
    }

    private async void OnE85(object? sender, RoutedEventArgs e)
    {
        if (!await Dialogs.YesNo(this, "Please note that Trionic 5 does not support bi-fuelling. Once your binary has been converted to E85 it will no longer run on regular petrol. " +
                "The wizard will alter ignition settings, cranking and afterstart fuelling and normal fuelling.\n\n" +
                "The coldstart and warmup factors are estimated, check them with a wideband lambda sensor.", "E85 wizard")) return;
        await Dialogs.Report(this, "Convert to E85 report", await Vm.ConvertToE85Async());
    }

    private async void OnBoostAdaption(object? sender, RoutedEventArgs e)
    {
        if (Vm.BoostAdaption() is not { } b) return;
        if (await Dialogs.Numbers(this, "Boost adaption range wizard",
                "Please note that this wizard will adjust the actual code in your binary! The boost adaption range should match the spool characteristics of your turbo.",
                ("Manual gearbox from (rpm)", b.ManualLow, 1000, 9000, 50), ("Manual gearbox upto (rpm)", b.ManualHigh, 1000, 9000, 50),
                ("Automatic gearbox from (rpm)", b.AutomaticLow, 1000, 9000, 50), ("Automatic gearbox upto (rpm)", b.AutomaticHigh, 1000, 9000, 50),
                ("Max. boost error (bar)", b.BoostError / 100m, 0.01m, 0.2m, 0.01m)) is not { } v) return;
        await Vm.SetBoostAdaptionAsync(new((int)v[0], (int)v[1], (int)v[2], (int)v[3], (int)System.Math.Round(v[4] * 100)));
    }

    private async void OnBoostBias(object? sender, RoutedEventArgs e)
    {
        if (Vm.BoostBiasStep() is not { } step)
        {
            Vm.ShowInfo("This file has no 16-bit boost bias map, its range can't be changed");
            return;
        }
        if (await Dialogs.Numbers(this, "Boost bias RPM range wizard",
                "Please note that this wizard will adjust the actual code in your binary! The boost bias map then runs from 2500 rpm up to 2500 + step × 300 rpm.",
                ("Axis step range", step, 5, 20, 1)) is { } v)
            await Vm.SetBoostBiasStepAsync((int)v[0]);
    }

    private async void OnRpmLimit(object? sender, RoutedEventArgs e)
    {
        if (Vm.RpmLimits() is not { } r) return;
        if (await Dialogs.Numbers(this, "RPM limiter wizard", "Welcome to the RPM limiter wizard",
                ("Software RPM limit", r.software, 5000, 10000, 100), ("Hardcoded RPM limit", r.hardcoded, 5000, 10000, 100)) is { } v)
            await Vm.SetRpmLimitsAsync((int)v[1], (int)v[0]);
    }

    // ---- Actions page ----

    protected override (string caption, string text) TransferWizard =>
        ("Data transfer wizard", "Welcome to the data transfer wizard\n\nThis wizard will help you transferring data from the currect binary to the target binary file.\n\n"
            + "Make sure the source and target binary files are for the same engine type and such to prevent problem in ignition advance and fuelling from occuring!\n\n"
            + "Please review the target binary for correctness after the wizard finishes.");

    private async void OnExamine(object? sender, RoutedEventArgs e) => await Dialogs.Report(this, "Examination report", Vm.Examine());

    private async void OnAnomalies(object? sender, RoutedEventArgs e) => await Dialogs.Report(this, "Anomaly report", Vm.Anomalies());

    /// <summary>Open a saved report: T5Suite read DevExpress .prnx files, the reports here are saved as text.</summary>
    private async void OnOpenReport(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.OpenFile(this, "Reports", "*.txt") is { } file)
            await Dialogs.Report(this, System.IO.Path.GetFileNameWithoutExtension(file), await System.IO.File.ReadAllLinesAsync(file));
    }

    private async void OnMergeFiles(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.OpenFileIn(this, "First part of binary", null, "Binary files", "*.bin") is not { } first
            || await Dialogs.OpenFileIn(this, "Second part of binary", System.IO.Path.GetDirectoryName(first), "Binary files", "*.bin") is not { } second
            || await Dialogs.SaveFile(this, "Binary files", "bin", "merged.bin") is not { } output) return;
        if (Trionic5Tools.T5Binary.Merge(await System.IO.File.ReadAllBytesAsync(first), await System.IO.File.ReadAllBytesAsync(second)) is not { } merged)
        {
            await Dialogs.Info(this, "File lengths don't match, unable to merge!");
            return;
        }
        await System.IO.File.WriteAllBytesAsync(output, merged);
        await Dialogs.Info(this, "Files merged successfully");
    }

    /// <summary>Split binary file: chip1.bin and chip2.bin next to the open file (overwritten, as T5Suite did).</summary>
    private async void OnSplitFile(object? sender, RoutedEventArgs e)
    {
        if (Vm.Binary is not { } bin) return;
        string dir = System.IO.Path.GetDirectoryName(bin.FileName) ?? "";
        var (chip1, chip2) = Trionic5Tools.T5Binary.Split(await System.IO.File.ReadAllBytesAsync(bin.FileName));
        await System.IO.File.WriteAllBytesAsync(System.IO.Path.Combine(dir, "chip1.bin"), chip1);
        await System.IO.File.WriteAllBytesAsync(System.IO.Path.Combine(dir, "chip2.bin"), chip2);
        await Dialogs.Info(this, "File split to chip1.bin and chip2.bin");
    }
}
