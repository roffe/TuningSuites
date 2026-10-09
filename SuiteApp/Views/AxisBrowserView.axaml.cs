using Avalonia.Controls;
using Avalonia.Input;
using CommonSuite;
using SuiteApp.ViewModels;

namespace SuiteApp.Views;

public partial class AxisBrowserView : UserControl
{
    public AxisBrowserView() => InitializeComponent();

    // the clicked column decides: an axis column opens that axis, the others the map
    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not AxisBrowserViewModel vm || Grid.SelectedItem is not AxisInfo row) return;
        string? header = Grid.CurrentColumn?.Header as string;
        vm.Open(header?.StartsWith("X axis") == true ? row.XAxis : header?.StartsWith("Y axis") == true ? row.YAxis : row.Symbol);
    }
}
