using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using CommonSuite;
using SuiteApp.Services;

namespace SuiteApp.Views;

/// <summary>A row of a suite's PartnumberCollection, its cells in the table's order; Marked colours the partnumber when its stock bin is in Binaries.</summary>
public sealed class PartNumberRow(DataRow row, Func<string, IBrush?> mark)
{
    public object?[] Cells { get; } = row.ItemArray.Select(v => v is DBNull ? null : v).ToArray();

    public string Carmodel { get; } = $"{row["Carmodel"]}";

    public string Enginetype { get; } = $"{row["Enginetype"]}";

    public string Partnumber { get; } = $"{row["Partnumber"]}";

    public IBrush? Marked { get; } = mark($"{row["Partnumber"]}");
}

/// <summary>One ECU's partnumber list: its grid, the colours it marks the library with, its rows as Export writes them.</summary>
public sealed record PartNumberTab(string Header, DataGrid Grid, IReadOnlyList<(string Text, IBrush Colour)> Legend, Func<IEnumerable<string>> Export);

/// <summary>
/// frmPartNumberList "Partnumber list" for all suites: the suite's PartnumberCollection as the old grid showed it (every column, captions
/// split as DevExpress did, grouped by car model and engine type with the grouped columns shown), the ones in Binaries coloured; Ok or
/// double-click picks one, Export writes the list as text. TxSuite shows a tab per ECU.
/// </summary>
public static class PartNumberListWindow
{
    /// <summary>T7Suite's and T8Suite's legend.</summary>
    public static readonly (string, IBrush)[] InLibrary = [("Available in library", Brushes.YellowGreen)];

    /// <summary>The table's rows; mark colours a partnumber (default: yellow green when its bin is in Binaries).</summary>
    public static PartNumberTab Tab(string header, DataTable table, Func<string, IBrush?>? mark = null, (string, IBrush)[]? legend = null)
    {
        if (mark == null)
        {
            var library = PartInfo.StockBinaryNames();
            mark = pn => library.Contains(pn) ? Brushes.YellowGreen : null;
        }
        // the table's order within each group, as the old grid
        var rows = table.Rows.Cast<DataRow>().Select(r => new PartNumberRow(r, mark)).OrderBy(r => r.Carmodel).ThenBy(r => r.Enginetype).ToList();
        var view = new Avalonia.Collections.DataGridCollectionView(rows);
        view.GroupDescriptions.Add(new Avalonia.Collections.DataGridPathGroupDescription(nameof(PartNumberRow.Carmodel)));
        view.GroupDescriptions.Add(new Avalonia.Collections.DataGridPathGroupDescription(nameof(PartNumberRow.Enginetype)));
        var grid = new DataGrid { ItemsSource = view, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal };
        // DevExpress' captions for the column names: "StockboostAUT" → "Stockboost AUT"
        string[] captions = table.Columns.Cast<DataColumn>().Select(c => Regex.Replace(c.ColumnName, "(?<=[a-z])(?=[A-Z])", " ")).ToArray();
        foreach (DataColumn column in table.Columns)
        {
            string cell = $"{nameof(PartNumberRow.Cells)}[{column.Ordinal}]";
            // one way: the columns' default two way binding writes the shown text back into the cells (From MY's ints become strings)
            var binding = new Avalonia.Data.Binding(cell) { Mode = Avalonia.Data.BindingMode.OneWay };
            grid.Columns.Add(column.ColumnName == "Partnumber"
                ? new DataGridTemplateColumn
                {
                    Header = captions[column.Ordinal],
                    SortMemberPath = nameof(PartNumberRow.Partnumber),
                    // bound: the cell is made before its row is set; the whole cell coloured, the text where a text column's is
                    CellTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<PartNumberRow>((_, _) => new Border
                    {
                        Padding = new Thickness(12, 0),
                        [!Border.BackgroundProperty] = new Avalonia.Data.Binding(nameof(PartNumberRow.Marked)),
                        Child = new TextBlock { VerticalAlignment = VerticalAlignment.Center, [!TextBlock.TextProperty] = new Avalonia.Data.Binding(nameof(PartNumberRow.Partnumber)) },
                    }),
                }
                : column.DataType == typeof(bool)
                    ? new DataGridCheckBoxColumn { Header = captions[column.Ordinal], Binding = binding, SortMemberPath = cell, CanUserSort = true }
                    : new DataGridTextColumn { Header = captions[column.Ordinal], Binding = binding, SortMemberPath = cell, CanUserSort = true });
        }

        // a header click sorts within the groups, which stay in order (Carmodel ▲, Enginetype ▲) as DevExpress' did
        grid.Sorting += (_, e) =>
        {
            string path = e.Column.SortMemberPath;
            var direction = view.SortDescriptions.FirstOrDefault(d => d.PropertyPath == path)?.Direction == ListSortDirection.Ascending
                ? ListSortDirection.Descending : ListSortDirection.Ascending;
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(DataGridSortDescription.FromPath(nameof(PartNumberRow.Carmodel)));
            view.SortDescriptions.Add(DataGridSortDescription.FromPath(nameof(PartNumberRow.Enginetype)));
            view.SortDescriptions.Add(DataGridSortDescription.FromPath(path, direction));
            e.Handled = true;
        };

        // starts folded, as DevExpress' grid did. After the grid's first layout (before it the grid knows no groups; a tab's grid has
        // its first when the tab is shown), from the top and the last group first, as SymbolListView folds
        void Fold(object? sender, EventArgs e)
        {
            if (view.Groups?.OfType<DataGridCollectionViewGroup>().ToList() is not { Count: > 0 } groups) return;
            grid.LayoutUpdated -= Fold;
            grid.ScrollIntoView(groups[0], null);
            for (int i = groups.Count - 1; i >= 0; i--) grid.CollapseRowGroup(groups[i], true);
        }
        grid.LayoutUpdated += Fold;

        // ponytail: tab separated with a header line; DevExpress' ExportToText also wrote the group rows
        IEnumerable<string> Export() => rows.Select(r => string.Join('\t', r.Cells.Select(v => $"{v}"))).Prepend(string.Join('\t', captions));
        return new PartNumberTab(header, grid, legend ?? InLibrary, Export);
    }

    /// <summary>The list; with more than one tab (TxSuite: one per ECU, selected in front) the selected tab's row is picked.</summary>
    public static Task<string?> Show(Window owner, int selected, params PartNumberTab[] tabs)
    {
        Window? window = null;
        TabControl? tabControl = tabs.Length > 1
            ? new TabControl { ItemsSource = tabs.Select(t => new TabItem { Header = t.Header, Content = t.Grid }).ToList(), SelectedIndex = selected }
            : null;
        PartNumberTab Current() => tabs[tabControl?.SelectedIndex ?? 0];

        void Pick() => window!.Close((Current().Grid.SelectedItem as PartNumberRow)?.Partnumber);

        async void Export()
        {
            if (await Dialogs.SaveFile(window!, "Export partnumber list", "Text files", "txt", null) is { } file) File.WriteAllLines(file, Current().Export());
        }

        foreach (var tab in tabs) tab.Grid.DoubleTapped += (_, _) => Pick();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
        foreach (var (caption, click) in new (string, Action)[] { ("Export", Export), ("Ok", Pick), ("Close", () => window!.Close(null)) })
        {
            var button = new Button { Content = caption, MinWidth = 80 };
            button.Click += (_, _) => click();
            buttons.Children.Add(button);
        }

        // the labels in their colours, one under the other as the old forms'; the tab in front's
        var legend = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Bottom };
        void ShowLegend()
        {
            legend.Children.Clear();
            foreach (var (text, colour) in Current().Legend)
                legend.Children.Add(new Border { Background = colour, Padding = new Thickness(4, 1), HorizontalAlignment = HorizontalAlignment.Left,
                    Child = new TextBlock { Text = text, Foreground = Brushes.Black } });
        }
        ShowLegend();
        if (tabControl != null) tabControl.SelectionChanged += (_, _) => ShowLegend();

        var bar = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(buttons, Avalonia.Controls.Dock.Right);
        bar.Children.Add(buttons);
        bar.Children.Add(legend);

        var panel = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(bar, Avalonia.Controls.Dock.Bottom);
        panel.Children.Add(bar);
        var group = new HeaderedContentControl { Header = "Partnumbers", Content = (Control?)tabControl ?? tabs[0].Grid };
        group.Classes.Add("group");
        panel.Children.Add(group);

        window = new Window
        {
            Title = "Partnumber list",
            Width = 1200,
            Height = 640,
            Content = panel,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = true,
        };
        // rows as the symbol list's (its text size from Settings), not Fluent's 15 px on 32: the old lists were dense
        window.Styles.Add(new Style(x => Selectors.Or(x.OfType<DataGridCell>(), x.OfType<DataGridRowGroupHeader>()))
        {
            Setters =
            {
                new Setter(TemplatedControl.FontSizeProperty, Resource(owner, "SuiteSymbolListFontSize", 12.0)),
                new Setter(Layoutable.MinHeightProperty, Resource(owner, "SuiteSymbolListRowHeight", 26.0)),
            },
        });

        return window.ShowDialog<string?>(owner);
    }

    private static double Resource(Window owner, string key, double fallback) =>
        owner.TryFindResource(key, out var value) && value is double d ? d : fallback;
}
