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

    // T8Suite's frmAbout
    protected override (string thanks, string support, string closing) AboutTexts =>
        ("Actitis H., Steve Hayes, Hook, mackan, MrAze, Sandy_rus, T5_Germany, Seb, Tomili, sourcode, J.K Nilsson, G-ice, General Failure and Mattias Claesson",
         "Currently no e-mail support, check out www.trionictuning.com and www.ecuproject.com", "Special thanks to Just4pLeisure.");

    // the file's VIN, from the last valid flash block
    private async void OnVinDecoder(object? sender, RoutedEventArgs e) =>
        await new VinDecoderWindow { DataContext = new VinDecoderViewModel(Vm.FirmwareInfo()?.ChassisId ?? "") }.ShowDialog(this);

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
}
