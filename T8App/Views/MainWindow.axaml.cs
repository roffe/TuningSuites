using Avalonia.Interactivity;
using SuiteApp.Views;
using T8App.ViewModels;

namespace T8App.Views;

/// <summary>T8Suite's main window: its menus (the ribbon's pages, groups and captions) and the actions only T8Suite has.</summary>
public partial class MainWindow : SuiteMainWindow
{
    public MainWindow() => InitializeComponent();

    private new T8MainWindowViewModel Vm => (T8MainWindowViewModel)DataContext!;

    protected override string BinaryFilesName => "Trionic 8 binary files";

    // T8Suite's frmAbout
    protected override (string thanks, string support, string closing) AboutTexts =>
        ("Actitis H., Steve Hayes, Hook, mackan, MrAze, Sandy_rus, T5_Germany, Seb, Tomili, sourcode, J.K Nilsson, G-ice, General Failure and Mattias Claesson",
         "Currently no e-mail support, check out www.trionictuning.com and www.ecuproject.com", "Special thanks to Just4pLeisure.");

    private async void OnFirmwareInformation(object? sender, RoutedEventArgs e)
    {
        if (Vm.FirmwareInfo() is { } info) await new FirmwareInfoWindow { DataContext = info }.ShowDialog(this);
    }
}
