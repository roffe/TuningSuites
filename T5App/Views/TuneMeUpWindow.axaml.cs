using Avalonia.Controls;
using Avalonia.Interactivity;

namespace T5App.Views;

public partial class TuneMeUpWindow : Window
{
    public TuneMeUpWindow() => InitializeComponent();

    private void OnOk(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
