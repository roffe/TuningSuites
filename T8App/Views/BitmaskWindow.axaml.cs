using Avalonia.Controls;
using Avalonia.Interactivity;

namespace T8App.Views;

public partial class BitmaskWindow : Window
{
    public BitmaskWindow() => InitializeComponent();

    private void OnOk(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
