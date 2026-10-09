using Avalonia.Controls;
using Avalonia.Interactivity;

namespace T7App.Views;

public partial class TcmWindow : Window
{
    public TcmWindow() => InitializeComponent();

    private void OnOk(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
