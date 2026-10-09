using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SuiteApp.Services;
using SuiteApp.ViewModels;

namespace SuiteApp.Views;

public partial class TuningPackageEditorView : UserControl
{
    public TuningPackageEditorView() => InitializeComponent();

    private TuningPackageEditorViewModel Vm => (TuningPackageEditorViewModel)DataContext!;

    private Window? Owner => TopLevel.GetTopLevel(this) as Window;

    // the main window's view model, also from a floating document window
    private MainWindowViewModel? Main => Owner?.DataContext as MainWindowViewModel ?? (Owner?.Owner as Window)?.DataContext as MainWindowViewModel;

    private async void OnOpen(object? sender, RoutedEventArgs e)
    {
        if (Owner is { } w && Main is { } main && await Dialogs.OpenFile(w, main.PackageFilesName, "*." + main.PackageExtension) is { } file) Vm.Open(file);
    }

    // the symbol list's selection stands in for the suites' drag and drop
    private void OnAdd(object? sender, RoutedEventArgs e)
    {
        if (Main is { } main) Vm.Add(main.SelectedSymbols);
    }

    private void OnRemove(object? sender, RoutedEventArgs e) => Vm.Remove(Grid.SelectedItems.OfType<TuningPackageRow>());

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        if (Owner is { } w && Main is { } main && await Dialogs.SaveFile(w, main.PackageFilesName, main.PackageExtension) is { } file) Vm.Save(file);
    }

    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (Grid.SelectedItem is TuningPackageRow row) Vm.OpenRow(row);
    }

    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete) return;
        Vm.Remove(Grid.SelectedItems.OfType<TuningPackageRow>());
        e.Handled = true;
    }
}
