using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SuiteApp.Views;

public partial class LogbookWindow : Window
{
    public LogbookWindow() => InitializeComponent();

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
