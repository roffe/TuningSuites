using Avalonia.Interactivity;
using SuiteApp.Services;
using SuiteApp.ViewModels;
using SuiteApp.Views;
using T8App.ViewModels;

namespace T8App.Views;

/// <summary>T8Suite's main window: its menus (the ribbon's pages, groups and captions) and the actions only T8Suite has.</summary>
public partial class MainView : SuiteMainView
{
    public MainView() => InitializeComponent();

    private new T8MainWindowViewModel Vm => (T8MainWindowViewModel)DataContext!;

    protected override string BinaryFilesName => "Trionic 8 binary files";

    protected override string OpenFileTitle => "Open a binary Trionic 8 file...";

    protected override string CompareFilesName => "Trionic 8 binaries";

    protected override System.Func<System.Threading.Tasks.Task<string?>>? BrowsePartNumbers => () => PartNumberListWindow.Show(Host, 0, PartNumberTab());

    /// <summary>frmPartNumberList: T8Suite's PartnumberCollection; TxSuite's T8 tab.</summary>
    public static PartNumberTab PartNumberTab() => PartNumberListWindow.Tab("T8", new T8SuitePro.PartnumberCollection().GeneratePartNumberCollection());

    // T8Suite's frmAbout list
    protected override string AboutThanks =>
        "Actitis H., Steve Hayes, Hook, mackan, MrAze, Sandy_rus, T5_Germany, Seb, Tomili, sourcode, J.K Nilsson, G-ice, General Failure, Mattias Claesson, Roffe and...";

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is T8MainWindowViewModel vm) vm.ShowBitmask = bits => new BitmaskWindow { DataContext = bits }.ShowDialog<bool>(Host);
    }

    private async void OnRecoverEcu(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.OpenFile(Host, "Recover ECU", "Binary files", ["*.bin"]) is { } file) await Vm.RecoverEcuAsync(file);
    }

    private async void OnEcuInformation(object? sender, RoutedEventArgs e)
    {
        if (await Vm.ReadEcuInfoAsync() is { } rows) new EcuInfoWindow(rows).Show(Host);
    }

    private async void OnSettings(object? sender, RoutedEventArgs e)
    {
        if (SettingsDialog != null)
        {
            if (!await SettingsDialog(this)) return;
        }
        else
        {
            var settings = new SettingsViewModel(Vm.Settings);
            if (!await new SettingsWindow { DataContext = settings }.ShowDialog<bool>(Host)) return;
            settings.Apply(Vm.Settings);
        }
        Vm.SettingsChanged();
        ApplyHideSymbolTable();
    }

    // txlogger's Firmware info edit: T8Suite's Firmware information (VIN, immobilizer code and software version)

    private async void OnFirmwareInformation(object? sender, RoutedEventArgs e)
    {
        if (Vm.FirmwareInfo() is not { } info) return;
        var firmware = new FirmwareInfoViewModel(info);
        var dialog = new FirmwareInfoWindow { DataContext = firmware };
        firmware.Hint += text => _ = Dialogs.Info(dialog, text, Vm.Caption);
        if (await dialog.ShowDialog<bool>(Host)) await Vm.ApplyFirmwareAsync(firmware.ToEdit());
    }

    // Actions → TEM editor / PID editor
    private async void OnPidEditor(object? sender, RoutedEventArgs e)
    {
        if (Vm.PidEditor((sender as Avalonia.Controls.Control)?.Tag is "tem") is not { } editor) return;
        if (await new PidEditorWindow { DataContext = editor }.ShowDialog<bool>(Host)) await Vm.ApplyPidEditorAsync(editor);
    }

    // File → Create binary from TIS file: the base bin (from Binaries), the TIS file (T8Suite saved the base's first part when that
    // was cancelled), then where to save it
    private async void OnCreateFromTis(object? sender, RoutedEventArgs e)
    {
        string binaries = CommonSuite.PartInfo.StockFolder("T8");
        if (await Dialogs.OpenFileIn(Host, "Select a binary file to base the new file on", binaries, "Binary files", ["*.bin"]) is not { } baseBin
            || !T8SuitePro.T8Binary.IsValidFile(baseBin)) return;
        string? tis = await Dialogs.OpenFile(Host, "Choose a TIS T8 file to build the new file with", "TIS T8 files", ["*.gbf", "*.s19"]);
        if (await Dialogs.SaveFile(Host, "Choose a filename for the new binary file", "Binary files", "bin", null) is { } target) Vm.CreateFromTis(baseBin, tis, target);
    }

    // Tuning → Tuning wizards → Tuning Wizard
    private async void OnTuningWizard(object? sender, RoutedEventArgs e)
    {
        if (Vm.TuningWizard() is { } wizard) await new TuningWizardWindow { DataContext = wizard }.ShowDialog(Host);
    }
}
