using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using CommonSuite;
using SuiteApp.Services;
using SuiteApp.ViewModels;

namespace SuiteApp.Views;

public partial class CompareResultsView : UserControl
{
    public CompareResultsView() => InitializeComponent();

    private CompareResultsViewModel Vm => (CompareResultsViewModel)DataContext!;

    private void OnDoubleTapped(object? sender, TappedEventArgs e) => Vm.Open(Vm.Selected);

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Vm.Open(Vm.Selected);
        e.Handled = true;
    }

    private void OnOpen(object? sender, RoutedEventArgs e) => Vm.Open(Vm.Selected);

    private void OnDifferenceMap(object? sender, RoutedEventArgs e) => Vm.ShowDifferenceMap(Vm.Selected);

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window w && await Dialogs.SaveFile(w, "Export to CSV", "CSV files", "csv", "diffexport.csv") is { } file) Vm.ExportCsv(file);
    }

    // exportAsTuningPackageToolStripMenuItem_Click: from the open or the compared file, its symbols offered all checked
    private async void OnExportPackage(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window w || Vm.Other is not { } other || await ChooseFile(w, Vm.Current, other) is not { } bin) return;
        var symbols = Vm.PackageSymbols(bin);
        var names = symbols.Select(sh => sh.SmartVarname).ToList();
        var selection = new TransferSelectionViewModel(names, names.ToHashSet()) { Prompt = "Select symbols to export..." };
        if (!await new TransferSelectionWindow { DataContext = selection, Title = "Export as tuning package" }.ShowDialog<bool>(w)) return;
        var chosen = selection.Selection;
        if (await Dialogs.SaveFile(w, "Export as tuning package", Vm.Owner.PackageFilesName, Vm.Owner.PackageExtension, null) is { } target)
            SymbolFiles.ExportPackage(bin, symbols.Where(sh => chosen.Contains(sh.SmartVarname)), target);
    }

    // frmFileToExportSelection: a button per file, named after it
    private static Task<SuiteBinary?> ChooseFile(Window owner, SuiteBinary original, SuiteBinary compare)
    {
        var dlg = new Window
        {
            Title = "Choose a file to export data from", Width = 570, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
        };
        var grid = new Grid { Margin = new Thickness(12), ColumnDefinitions = new("96,*"), RowDefinitions = new("Auto,Auto,Auto"), RowSpacing = 6 };
        void Add(Control c, int row, int column)
        {
            Grid.SetRow(c, row);
            Grid.SetColumn(c, column);
            grid.Children.Add(c);
        }
        foreach (var (row, label, bin) in new[] { (0, "Original file", original), (1, "Compare file", compare) })
        {
            // a TextBlock: a string's underscore would be an access key
            var b = new Button
            {
                Content = new TextBlock { Text = Path.GetFileNameWithoutExtension(bin.FileName) }, Height = 39,
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            };
            b.Click += (_, _) => dlg.Close(bin);
            Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }, row, 0);
            Add(b, row, 1);
        }
        var cancel = new Button { Content = "Cancel", MinWidth = 81, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right };
        cancel.Click += (_, _) => dlg.Close(null);
        Add(cancel, 2, 1);
        dlg.Content = grid;
        return dlg.ShowDialog<SuiteBinary?>(owner);
    }
}
