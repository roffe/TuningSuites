using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SuiteApp.Services;
using T7App.ViewModels;

namespace T7App.Views;

public partial class TuningPackageEditorView : UserControl
{
    public TuningPackageEditorView() => InitializeComponent();

    private TuningPackageEditorViewModel Vm => (TuningPackageEditorViewModel)DataContext!;

    private Window? Owner => TopLevel.GetTopLevel(this) as Window;

    private async void OnOpen(object? sender, RoutedEventArgs e)
    {
        if (Owner is { } w && await Dialogs.OpenFile(w, "Trionic 7 packages", "*.t7p") is { } file) Vm.Open(file);
    }

    // the symbol list's selection stands in for T7Suite's drag and drop
    private void OnAdd(object? sender, RoutedEventArgs e)
    {
        if (Owner?.DataContext is MainWindowViewModel main) Vm.Add(main.SelectedSymbols);
    }

    private void OnRemove(object? sender, RoutedEventArgs e) => Vm.Remove(Grid.SelectedItems.OfType<TuningPackageRow>());

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        if (Owner is { } w && await Dialogs.SaveFile(w, "Trionic 7 packages", "t7p") is { } file) Vm.Save(file);
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
