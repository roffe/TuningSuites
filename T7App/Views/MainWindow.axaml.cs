using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using T7App.Services;
using T7App.ViewModels;

namespace T7App.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MainWindowViewModel vm) vm.Info += text => _ = Dialogs.Info(this, text, "T7Suite");
    }

    private async void OnOpenFile(object? sender, RoutedEventArgs e)
    {
        string? path = await Dialogs.OpenFile(this, "Trionic 7 binary or Motorola S19", "*.bin", "*.s19");
        if (path != null) await Vm.OpenFileAsync(path, true);
    }

    private async void OnFirmwareInformation(object? sender, RoutedEventArgs e)
    {
        if (Vm.FirmwareInfo() is { } info) await new FirmwareInfoWindow { DataContext = info }.ShowDialog(this);
    }

    private void OnSymbolDoubleTapped(object? sender, TappedEventArgs e) => Vm.OpenSymbolCommand.Execute(Vm.SelectedSymbol);

    private void OnSymbolKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Vm.OpenSymbolCommand.Execute(Vm.SelectedSymbol);
        e.Handled = true;
    }
}
