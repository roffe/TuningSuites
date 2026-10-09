using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SuiteApp.Services;
using SuiteApp.ViewModels;

namespace SuiteApp.Views;

/// <summary>Lookup partnumber; closes with what to do with the stock file: "open", "compare" or "create:&lt;new file&gt;".</summary>
public partial class PartLookupWindow : Window
{
    public PartLookupWindow() => InitializeComponent();

    private PartLookupViewModel Vm => (PartLookupViewModel)DataContext!;

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Vm.Lookup();
    }

    private void OnLookup(object? sender, RoutedEventArgs e) => Vm.Lookup();

    private void OnOpenFile(object? sender, RoutedEventArgs e) => Close("open");

    private void OnCompare(object? sender, RoutedEventArgs e) => Close("compare");

    private async void OnCreate(object? sender, RoutedEventArgs e)
    {
        if (await Dialogs.SaveFile(this, "binary files", "bin", Vm.PartNumber + ".bin") is { } file) Close("create:" + file);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close(null);
}
