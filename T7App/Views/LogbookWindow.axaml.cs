using Avalonia.Controls;
using Avalonia.Interactivity;

namespace T7App.Views;

public partial class LogbookWindow : Window
{
    public LogbookWindow() => InitializeComponent();

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
