using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SuiteApp.Views;

public partial class RealtimeSymbolWindow : Window
{
    public RealtimeSymbolWindow() => InitializeComponent();

    private void OnOk(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
