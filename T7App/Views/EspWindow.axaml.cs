using Avalonia.Controls;
using Avalonia.Interactivity;
using T7App.ViewModels;

namespace T7App.Views;

public partial class EspWindow : Window
{
    public EspWindow() => InitializeComponent();

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EspViewModel { Value: not null }) Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
