using Avalonia.Controls;
using Avalonia.Interactivity;
using SuiteApp.Services;
using T7App.ViewModels;

namespace T7App.Views;

public partial class ProjectPropertiesWindow : Window
{
    public ProjectPropertiesWindow() => InitializeComponent();

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.OpenFile(this, "Binary files", "*.bin") is { } file) ((ProjectPropertiesViewModel)DataContext!).BinaryFile = file;
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
