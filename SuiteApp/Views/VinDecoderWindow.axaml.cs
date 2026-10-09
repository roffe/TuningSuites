using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SuiteApp.ViewModels;

namespace SuiteApp.Views;

public partial class VinDecoderWindow : Window
{
    public VinDecoderWindow() => InitializeComponent();

    private VinDecoderViewModel Vm => (VinDecoderViewModel)DataContext!;

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Vm.Decode();
        e.Handled = true;
    }

    private void OnDecode(object? sender, RoutedEventArgs e) => Vm.Decode();

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
