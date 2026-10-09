using Avalonia.Controls;
using Avalonia.Interactivity;
using T7App.ViewModels;

namespace T7App.Views;

public partial class TransferSelectionWindow : Window
{
    public TransferSelectionWindow() => InitializeComponent();

    private TransferSelectionViewModel Vm => (TransferSelectionViewModel)DataContext!;

    private void OnAll(object? sender, RoutedEventArgs e) => Vm.SetAll(true);

    private void OnNone(object? sender, RoutedEventArgs e) => Vm.SetAll(false);

    private void OnOk(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
