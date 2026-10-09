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
        // the column headers get T7Suite's grid menu instead of the rows' one
        SymbolGrid.AddHandler(ContextRequestedEvent, OnContextRequested, RoutingStrategies.Tunnel);
    }

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (e.Source is not Avalonia.Visual source
            || Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<Avalonia.Controls.DataGridColumnHeader>(source, true) is not { } header) return;
        DataGridColumn? column = SymbolGrid.Columns.FirstOrDefault(c => Equals(c.Header, header.Content));
        if (column == null || Vm is not { } vm) return;
        e.Handled = true;
        HeaderMenu(vm, column).Open(header);
    }

    // the DevExpress column menu, as far as it applies here
    private ContextMenu HeaderMenu(MainWindowViewModel vm, DataGridColumn column)
    {
        string? path = column.SortMemberPath;
        MenuItem Item(string text, System.Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = text, IsEnabled = enabled };
            item.Click += (_, _) => action();
            return item;
        }
        var chooser = new MenuItem { Header = "Column chooser" };
        foreach (DataGridColumn c in SymbolGrid.Columns)
        {
            var item = new MenuItem { Header = c.Header, ToggleType = MenuItemToggleType.CheckBox, IsChecked = c.IsVisible };
            item.Click += (_, _) => c.IsVisible = !c.IsVisible || SymbolGrid.Columns.Count(x => x.IsVisible) == 1;
            chooser.Items.Add(item);
        }
        var menu = new ContextMenu();
        menu.Items.Add(Item("Sort ascending", () => vm.SortSymbols(path), path != null));
        menu.Items.Add(Item("Sort descending", () => vm.SortSymbols(path, true), path != null));
        menu.Items.Add(Item("Clear sorting", () => vm.SortSymbols(null)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Group by this column", () => vm.GroupSymbols(path), path != null));
        menu.Items.Add(Item("Group by category", () => vm.GroupSymbols(nameof(SymbolHelper.Category))));
        menu.Items.Add(Item("No grouping", () => vm.GroupSymbols(null)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Hide this column", () => column.IsVisible = false, SymbolGrid.Columns.Count(c => c.IsVisible) > 1));
        menu.Items.Add(chooser);
        menu.Items.Add(Item("Best fit", () => column.Width = DataGridLength.Auto));
        menu.Items.Add(Item("Best fit (all columns)", () => { foreach (DataGridColumn c in SymbolGrid.Columns) c.Width = DataGridLength.Auto; }));
        menu.Items.Add(new Separator());
        var filterRow = new MenuItem { Header = "Show auto filter row", ToggleType = MenuItemToggleType.CheckBox, IsChecked = vm.ShowFilterRow };
        filterRow.Click += (_, _) => vm.ShowFilterRow = !vm.ShowFilterRow;
        menu.Items.Add(filterRow);
        return menu;
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
