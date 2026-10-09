using Avalonia.Controls;
using Avalonia.Interactivity;
using T7App.ViewModels;

namespace T7App.Views;

public partial class MyMapsWindow : Window
{
    public MyMapsWindow() => InitializeComponent();

    private MyMapsViewModel Vm => (MyMapsViewModel)DataContext!;

    private void OnAdd(object? sender, RoutedEventArgs e) => Grid.SelectedItem = Vm.Add();

    private void OnRemove(object? sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is MyMapRow row) Vm.Rows.Remove(row);
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
