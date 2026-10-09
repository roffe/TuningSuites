using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SuiteApp.Services;
using T7App.ViewModels;

namespace T7App.Views;

public partial class CompareResultsView : UserControl
{
    public CompareResultsView() => InitializeComponent();

    private CompareResultsViewModel Vm => (CompareResultsViewModel)DataContext!;

    private void OnDoubleTapped(object? sender, TappedEventArgs e) => Vm.Open(Vm.Selected);

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Vm.Open(Vm.Selected);
        e.Handled = true;
    }

    private void OnOpen(object? sender, RoutedEventArgs e) => Vm.Open(Vm.Selected);

    private void OnDifferenceMap(object? sender, RoutedEventArgs e) => Vm.ShowDifferenceMap(Vm.Selected);

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window w && await Dialogs.SaveFile(w, "CSV files", "csv", "diffexport.csv") is { } file) Vm.ExportCsv(file);
    }
}
