using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using T8App.ViewModels;
using T8SuitePro;

namespace T8App.Views;

/// <summary>Closes with true on Ok.</summary>
public partial class FirmwareInfoWindow : Window
{
    public FirmwareInfoWindow() => InitializeComponent();

    private FirmwareInfoViewModel Vm => (FirmwareInfoViewModel)DataContext!;

    private void OnSoftwareVersionLabel(object? sender, TappedEventArgs e) => Vm.StartSoftwareVersionEdit();

    private void OnVinLabel(object? sender, TappedEventArgs e) => Vm.StartVinAndImmoEdit();

    private void OnClearVin(object? sender, RoutedEventArgs e) => Vm.ClearVin();

    // the browser reads the file again, as T8Suite did
    private async void OnFlashBlocks(object? sender, RoutedEventArgs e)
    {
        if (Owner is Window { DataContext: T8MainWindowViewModel { Binary: { } bin } })
            await new FlashBlocksWindow(FirmwareInfo.FlashBlocks(bin.FileName)).ShowDialog(this);
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
