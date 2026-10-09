using Avalonia.Controls;
using Avalonia.Interactivity;

namespace T7App.Views;

public partial class FirmwareInfoWindow : Window
{
    public FirmwareInfoWindow() => InitializeComponent();

    private void OnOk(object? sender, RoutedEventArgs e) => Close();
}
