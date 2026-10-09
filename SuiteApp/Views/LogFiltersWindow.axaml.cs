using Avalonia.Controls;
using Avalonia.Interactivity;
using SuiteApp.ViewModels;

namespace SuiteApp.Views;

public partial class LogFiltersWindow : Window
{
    public LogFiltersWindow() => InitializeComponent();

    private LogFiltersViewModel Vm => (LogFiltersViewModel)DataContext!;

    private void OnAdd(object? sender, RoutedEventArgs e)
    {
        var row = new LogFilterRow();
        Vm.Rows.Add(row);
        Grid.SelectedItem = row;
    }

    private void OnRemove(object? sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is LogFilterRow row) Vm.Rows.Remove(row);
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
