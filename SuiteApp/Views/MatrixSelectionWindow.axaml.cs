using Avalonia.Controls;
using Avalonia.Interactivity;
using SuiteApp.ViewModels;

namespace SuiteApp.Views;

public partial class MatrixSelectionWindow : Window
{
    public MatrixSelectionWindow() => InitializeComponent();

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MatrixSelectionViewModel { IsComplete: true }) Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
