using Avalonia.Interactivity;
using SuiteApp.Services;
using SuiteApp.ViewModels;
using SuiteApp.Views;
using T8App.ViewModels;

namespace T8App.Views;

/// <summary>T8Suite's main window: its menus (the ribbon's pages, groups and captions) and the actions only T8Suite has.</summary>
public partial class MainWindow : SuiteMainWindow
{
    public MainWindow() => InitializeComponent();

    private new T8MainWindowViewModel Vm => (T8MainWindowViewModel)DataContext!;

    protected override string BinaryFilesName => "Trionic 8 binary files";

    protected override string CompareFilesName => "Trionic 8 binaries";

    protected override System.Func<System.Threading.Tasks.Task<string?>>? BrowsePartNumbers => () => PartNumberListWindow.Show(this, Vm.LookupPartNumber);

    // T8Suite's frmAbout list
    protected override string AboutThanks =>
        "Actitis H., Steve Hayes, Hook, mackan, MrAze, Sandy_rus, T5_Germany, Seb, Tomili, sourcode, J.K Nilsson, G-ice, General Failure, Mattias Claesson, Roffe and...";

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is T8MainWindowViewModel vm) vm.ShowBitmask = bits => new BitmaskWindow { DataContext = bits }.ShowDialog<bool>(this);
    }

    private async void OnRecoverEcu(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.OpenFile(this, "Binary files", "*.bin") is { } file) await Vm.RecoverEcuAsync(file);
    }

    private async void OnEcuInformation(object? sender, RoutedEventArgs e)
    {
        if (await Vm.ReadEcuInfoAsync() is { } rows) new EcuInfoWindow(rows).Show(this);
    }

    private async void OnSettings(object? sender, RoutedEventArgs e)
    {
        var settings = new SettingsViewModel(Vm.Settings);
        if (!await new SettingsWindow { DataContext = settings }.ShowDialog<bool>(this)) return;
        settings.Apply(Vm.Settings);
        Vm.SettingsChanged();
        ApplyHideSymbolTable();
    }

    private async void OnFirmwareInformation(object? sender, RoutedEventArgs e)
    {
        if (Vm.FirmwareInfo() is not { } info) return;
        var firmware = new FirmwareInfoViewModel(info);
        var dialog = new FirmwareInfoWindow { DataContext = firmware };
        firmware.Hint += text => _ = Dialogs.Info(dialog, text, Vm.Caption);
        if (await dialog.ShowDialog<bool>(this)) await Vm.ApplyFirmwareAsync(firmware.ToEdit());
    }

    // Actions → TEM editor / PID editor
    private async void OnPidEditor(object? sender, RoutedEventArgs e)
    {
        if (Vm.PidEditor((sender as Avalonia.Controls.Control)?.Tag is "tem") is not { } editor) return;
        if (await new PidEditorWindow { DataContext = editor }.ShowDialog<bool>(this)) await Vm.ApplyPidEditorAsync(editor);
    }

    // File → Create binary from TIS file: the base bin (from Binaries), the TIS file (T8Suite saved the base's first part when that
    // was cancelled), then where to save it
    private async void OnCreateFromTis(object? sender, RoutedEventArgs e)
    {
        string binaries = System.IO.Path.Combine(System.AppContext.BaseDirectory, "Binaries");
        if (await Dialogs.OpenFileIn(this, "Select a binary file to base the new file on", binaries, "Binary files", "*.bin") is not { } baseBin
            || !T8SuitePro.T8Binary.IsValidFile(baseBin)) return;
        string? tis = await Dialogs.OpenFileIn(this, "Choose a TIS T8 file to build the new file with", null, "TIS T8 files", "*.gbf", "*.s19");
        if (await Dialogs.SaveFile(this, "Binary files", "bin", title: "Choose a filename for the new binary file") is { } target) Vm.CreateFromTis(baseBin, tis, target);
    }

    // Tuning → Tuning wizards → Tuning Wizard
    private async void OnTuningWizard(object? sender, RoutedEventArgs e)
    {
        if (Vm.TuningWizard() is { } wizard) await new TuningWizardWindow { DataContext = wizard }.ShowDialog(this);
    }
}
