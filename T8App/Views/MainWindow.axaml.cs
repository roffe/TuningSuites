using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace T8App.Views;

// ponytail: the scaffold's window; T7App's main window, moved into SuiteApp, replaces it once T8 opens bins
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = "T8SuitePro v" + typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
    }

    private void OnExit(object? sender, RoutedEventArgs e) => Close();
}
