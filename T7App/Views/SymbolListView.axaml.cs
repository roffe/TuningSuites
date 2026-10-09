using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CommonSuite;
using T7App.ViewModels;

namespace T7App.Views;

/// <summary>The symbol list, the workspace's dockable tool pane; the context menu's actions live in the main window.</summary>
public partial class SymbolListView : UserControl
{
    private bool m_editingSymbol;

    public SymbolListView()
    {
        InitializeComponent();
        // Enter opens the symbol; the grid would move to the next row, so catch it on the way down, unless a cell is being edited
        SymbolGrid.AddHandler(KeyDownEvent, OnSymbolKeyDown, RoutingStrategies.Tunnel);
        SymbolGrid.BeginningEdit += (_, _) => m_editingSymbol = true;
    }

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;
    private MainWindow? Main => TopLevel.GetTopLevel(this) as MainWindow;

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Vm is { } vm) vm.SelectedSymbols = SymbolGrid.SelectedItems.OfType<SymbolHelper>().ToList();
    }

    private void OnSymbolCellEditEnded(object? sender, DataGridCellEditEndedEventArgs e)
    {
        m_editingSymbol = false;
        if (e.EditAction == DataGridEditAction.Commit) Vm?.SaveUserDescriptions();
    }

    private void OnSymbolDoubleTapped(object? sender, TappedEventArgs e) => Vm?.OpenSymbolCommand.Execute(Vm.SelectedSymbol);

    private void OnSymbolKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || m_editingSymbol || Vm is not { } vm) return;
        vm.OpenSymbolCommand.Execute(vm.SelectedSymbol);
        e.Handled = true;
    }

    private void OnReadSymbolFromEcu(object? sender, RoutedEventArgs e) => Main?.OnReadSymbolFromEcu(sender, e);
    private void OnAddToRealtime(object? sender, RoutedEventArgs e) => Main?.OnAddToRealtime(sender, e);
    private void OnReadFromSramFile(object? sender, RoutedEventArgs e) => Main?.OnReadFromSramFile(sender, e);
    private void OnAddToMyMaps(object? sender, RoutedEventArgs e) => Main?.OnAddToMyMaps(sender, e);
    private void OnExportPackage(object? sender, RoutedEventArgs e) => Main?.OnExportPackage(sender, e);
    private void OnExportFixedPackage(object? sender, RoutedEventArgs e) => Main?.OnExportFixedPackage(sender, e);
    private void OnBrowseAxes(object? sender, RoutedEventArgs e)
    {
        if (Vm?.SelectedSymbol is { } sh) Vm.BrowseAxes(sh.SmartVarname);
    }

    private void OnExportSymbolCsv(object? sender, RoutedEventArgs e) => Main?.OnExportSymbolCsv(sender, e);
}
