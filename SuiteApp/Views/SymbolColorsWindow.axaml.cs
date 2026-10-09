using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SuiteApp.Views;

/// <summary>Set symbol colors (T7Suite's frmPlotSelection in its symbol colours role).</summary>
public partial class SymbolColorsWindow : Window
{
    public SymbolColorsWindow() => InitializeComponent();

    private void OnOk(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
