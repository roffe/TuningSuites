using Avalonia.Controls;
using Avalonia.Interactivity;
using T7App.Services;
using T7App.ViewModels;

namespace T7App.Views;

public partial class SidInfoWindow : Window
{
    public SidInfoWindow() => InitializeComponent();

    private SidInfoViewModel Vm => (SidInfoViewModel)DataContext!;

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.SaveFile(this, "SID settings", "sid") is { } file) Vm.Export(file);
    }

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.OpenFile(this, "SID settings", "*.sid") is { } file && !Vm.Import(file))
            await Dialogs.Info(this, "Unable to import SIDi settings with a different length!");
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
