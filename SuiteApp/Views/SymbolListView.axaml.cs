using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CommonSuite;
using SuiteApp.ViewModels;

namespace SuiteApp.Views;

/// <summary>The symbol list, the workspace's dockable tool pane. The rows' menu is the suite's (the window's SymbolListMenu).</summary>
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

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (Vm is not { } vm) return;
        SymbolColorConverter.Enabled = vm.ColorSymbolNames;
        FilterChoice.IsVisible = vm.SymbolFilters.Count > 0;
        // the suite's columns in its order, the others dropped; hidden ones stay in the column chooser
        var wanted = vm.SymbolColumns;
        foreach (DataGridColumn c in SymbolGrid.Columns.ToList())
            if (wanted.All(w => w.Path != c.SortMemberPath)) SymbolGrid.Columns.Remove(c);
        for (int i = 0; i < wanted.Count; i++)
        {
            if (SymbolGrid.Columns.FirstOrDefault(c => c.SortMemberPath == wanted[i].Path) is not { } column) continue;
            column.DisplayIndex = i;
            column.Header = wanted[i].Header;
            column.IsVisible = wanted[i].Visible;
        }
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Vm is { } vm) vm.SelectedSymbols = SymbolGrid.SelectedItems.OfType<SymbolHelper>().ToList();
    }

    private void OnSymbolCellEditEnded(object? sender, DataGridCellEditEndedEventArgs e)
    {
        m_editingSymbol = false;
        if (e.EditAction == DataGridEditAction.Commit) Vm?.SaveUserDescriptions();
    }

    // T8Suite's map preview popup (frmMapHelper): the hovered name's map as a table, read only; nothing when the suite has none
    private void OnNameToolTipOpening(object? sender, CancelRoutedEventArgs e)
    {
        if (sender is not Control c || c.DataContext is not SymbolHelper sh || Vm?.MapPreview(sh) is not { } preview)
        {
            e.Cancel = true;
            return;
        }
        ToolTip.SetTip(c, new MapControls.MapGrid
        {
            Map = preview.Map, ViewType = preview.ViewType, IsRedWhite = preview.IsRedWhite, DisableColors = preview.DisableColors, IsReadOnly = true,
        });
    }

    private void OnSymbolDoubleTapped(object? sender, TappedEventArgs e) => Vm?.OpenSymbolCommand.Execute(Vm.SelectedSymbol);

    private void OnSymbolKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || m_editingSymbol || Vm is not { } vm) return;
        vm.OpenSymbolCommand.Execute(vm.SelectedSymbol);
        e.Handled = true;
    }
}
