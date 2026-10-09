using Avalonia.Controls;
using Avalonia.Interactivity;
using T7App.Services;
using T7App.ViewModels;

namespace T7App.Views;

/// <summary>Closes with true on Ok; the caller applies the changes.</summary>
public partial class FirmwareInfoWindow : Window
{
    public FirmwareInfoWindow() => InitializeComponent();

    private void OnOk(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        string? file = await Dialogs.OpenFile(this, "Select a file to extract VIN and Immobilizer code from", "*.bin");
        if (file != null) ((FirmwareInfoViewModel)DataContext!).Import(file);
    }

    private void OnUndoImport(object? sender, RoutedEventArgs e) => ((FirmwareInfoViewModel)DataContext!).UndoImport();
}
