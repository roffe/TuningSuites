using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CommonSuite;
using SuiteApp.ViewModels;

namespace SuiteApp.Views;

/// <summary>The symbol list, the workspace's dockable tool pane. The rows' menu is the suite's (the window's SymbolListMenu).</summary>
public partial class SymbolListView : UserControl
{
    private bool m_editingSymbol;
    private MainWindowViewModel? m_vm;
    private DataGridCollectionView? m_view;
    // what the grid's next layout does to the groups: fold them all (a new file, a regroup), or see whether a search started or ended
    private bool m_foldAll, m_refreshed;
    // every group unfolded for a search
    private bool m_searchUnfolded;

    public SymbolListView()
    {
        InitializeComponent();
        // Enter opens the symbol; the grid would move to the next row, so catch it on the way down, unless a cell is being edited
        SymbolGrid.AddHandler(KeyDownEvent, OnSymbolKeyDown, RoutingStrategies.Tunnel);
        SymbolGrid.BeginningEdit += (_, _) => m_editingSymbol = true;
        // the column headers get T7Suite's grid menu instead of the rows' one
        SymbolGrid.AddHandler(ContextRequestedEvent, OnContextRequested, RoutingStrategies.Tunnel);
        SymbolGrid.ColumnReordered += (_, _) => UpdateFilterColumns();
        SymbolGrid.LayoutUpdated += OnGridLayoutUpdated;
    }

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        // a column's header, or the empty header space right of the last one (the menu without the column's own items)
        if (e.Source is not Avalonia.Visual source || Vm is not { } vm
            || Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<Avalonia.Controls.Primitives.DataGridColumnHeadersPresenter>(source, true) is not { } headers)
            return;
        var header = Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<Avalonia.Controls.DataGridColumnHeader>(source, true);
        DataGridColumn? column = header == null ? null : SymbolGrid.Columns.FirstOrDefault(c => Equals(c.Header, header.Content));
        e.Handled = true;
        HeaderMenu(vm, column).Open((Control?)header ?? headers);
    }

    // the DevExpress column menu, as far as it applies here; no column: the empty header space
    private ContextMenu HeaderMenu(MainWindowViewModel vm, DataGridColumn? column)
    {
        string? path = column?.SortMemberPath;
        // ticked: what is on already
        MenuItem Item(string text, System.Action action, bool enabled = true, bool? on = null)
        {
            var item = new MenuItem { Header = text, IsEnabled = enabled };
            if (on is { } ticked)
            {
                item.ToggleType = MenuItemToggleType.CheckBox;
                item.IsChecked = ticked;
            }
            item.Click += (_, _) => action();
            return item;
        }
        var chooser = new MenuItem { Header = "Column chooser" };
        foreach (DataGridColumn c in SymbolGrid.Columns)
        {
            var item = new MenuItem { Header = c.Header, ToggleType = MenuItemToggleType.CheckBox, IsChecked = c.IsVisible };
            item.Click += (_, _) =>
            {
                c.IsVisible = !c.IsVisible || SymbolGrid.Columns.Count(x => x.IsVisible) == 1;
                UpdateFilterColumns();
            };
            chooser.Items.Add(item);
        }
        var menu = new ContextMenu();
        menu.Items.Add(Item("Sort ascending", () => vm.SortSymbols(path), path != null, vm.IsSortedBy(path, false)));
        menu.Items.Add(Item("Sort descending", () => vm.SortSymbols(path, true), path != null, vm.IsSortedBy(path, true)));
        menu.Items.Add(Item("Clear sorting", () => vm.SortSymbols(null)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Group by this column", () => Group(vm, path), path != null, path != null && vm.IsGroupedBy(path)));
        menu.Items.Add(Item("Group by category", () => Group(vm, nameof(SymbolHelper.Category)), true, vm.IsGroupedBy(nameof(SymbolHelper.Category))));
        menu.Items.Add(Item("No grouping", () => vm.GroupSymbols(null), true, vm.IsGroupedBy(null)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Hide this column", () =>
        {
            column!.IsVisible = false;
            UpdateFilterColumns();
        }, column != null && SymbolGrid.Columns.Count(c => c.IsVisible) > 1));
        menu.Items.Add(chooser);
        menu.Items.Add(Item("Best fit", () => column!.Width = DataGridLength.Auto, column != null));
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
        if (m_vm != null) m_vm.PropertyChanged -= OnVmPropertyChanged;
        m_vm = Vm;
        // Dock builds the list when it first shows, maybe with a file open already
        WatchSymbols();
        if (Vm is not { } vm) return;
        vm.PropertyChanged += OnVmPropertyChanged;
        vm.EndSymbolEdit = () => SymbolGrid.CommitEdit(DataGridEditingUnit.Row, true);
        SymbolColorConverter.Enabled = vm.ColorSymbolNames;
        CategoryColorConverter.Enabled = vm.ColorDescriptionsByCategory;
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
        UpdateFilterColumns();
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.Symbols)) WatchSymbols();
        else if (e.PropertyName == nameof(MainWindowViewModel.SelectedSymbol) && m_vm?.SelectedSymbol is { } sh) ShowSymbol(sh);
    }

    private static readonly Pointer s_wheel = new(Pointer.GetNextFreeId(), PointerType.Mouse, true);

    // the selected symbol (the map in front too) unfolds its category. One chosen outside the list and not in view goes to the top,
    // where the eye looks for it (ScrollIntoView leaves a row from below at the bottom edge); a click or key in the list moves nothing
    private void ShowSymbol(SymbolHelper sh)
    {
        bool inView = ShownRow(sh) is { } shown && shown.Bounds.Y >= 0 && shown.Bounds.Bottom <= ((Visual)shown.GetVisualParent()!).Bounds.Height;
        SymbolGrid.ScrollIntoView(sh, null);
        if (inView || SymbolGrid.IsKeyboardFocusWithin) return;
        SymbolGrid.UpdateLayout();
        // the grid has no offset to set: scroll on by pixels as its mouse wheel does, which stops at the list's end
        if (ShownRow(sh) is { Bounds.Y: > 0 } row)
            SymbolGrid.RaiseEvent(new PointerWheelEventArgs(SymbolGrid, s_wheel, SymbolGrid, default, 0, PointerPointProperties.None, KeyModifiers.None, new Avalonia.Vector(0, -row.Bounds.Y / 50)));
    }

    private DataGridRow? ShownRow(SymbolHelper sh) => SymbolGrid.GetVisualDescendants().OfType<DataGridRow>().FirstOrDefault(r => r.IsVisible && r.DataContext == sh);

    // a new file starts folded
    private void WatchSymbols()
    {
        if (m_view != null) m_view.CollectionChanged -= OnSymbolsChanged;
        m_view = m_vm?.Symbols;
        if (m_view != null) m_view.CollectionChanged += OnSymbolsChanged;
        m_foldAll = true;
        m_searchUnfolded = false;
    }

    // a refresh keeps the folds (the grid matches its groups by key), but a search starting or ending changes them
    private void OnSymbolsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset) m_refreshed = true;
    }

    private void Group(MainWindowViewModel vm, string? path)
    {
        vm.GroupSymbols(path);
        m_foldAll = true;
    }

    // after the grid's layout: before it the grid knows no groups (a file auto-loaded at startup), and folding inside its own
    // reset breaks it. A search unfolds every category so its matches show; its end folds them again but the selected symbol's.
    // ponytail: that also folds what was opened by hand before the search; remember the folds from before it if that bothers
    private void OnGridLayoutUpdated(object? sender, System.EventArgs e)
    {
        if (!m_foldAll && !m_refreshed) return;
        bool all = m_foldAll;
        m_foldAll = m_refreshed = false;
        if (m_vm is not { Symbols: { } view } vm) return;
        bool searching = !string.IsNullOrWhiteSpace(vm.SearchText) || vm.ColumnFilters.Any(f => !string.IsNullOrWhiteSpace(f.Text));
        if (searching == m_searchUnfolded && !all) return;
        m_searchUnfolded = searching;
        List<DataGridCollectionViewGroup> groups = [.. view.Groups?.OfType<DataGridCollectionViewGroup>() ?? []];
        if (groups.Count == 0) return;
        // (un)folding makes the group's header current, dropping the selected row
        SymbolHelper? selected = vm.SelectedSymbol;
        // from the top, the last first: the grid leaves a folded group's rows shown when its header is scrolled above them, and a
        // fold moves the groups below it before the grid has laid them out again
        SymbolGrid.ScrollIntoView(groups[0], null);
        for (int i = groups.Count - 1; i >= 0; i--)
        {
            if (searching) SymbolGrid.ExpandRowGroup(groups[i], true);
            else SymbolGrid.CollapseRowGroup(groups[i], true);
        }
        if (selected == null) return;
        SymbolGrid.ScrollIntoView(selected, null);
        vm.SelectedSymbol = selected;
    }

    // the auto filter row's boxes: the shown columns in the grid's order
    private void UpdateFilterColumns() =>
        Vm?.SetFilterColumns(SymbolGrid.Columns.Where(c => c.IsVisible).OrderBy(c => c.DisplayIndex).Select(c => c.SortMemberPath));

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
