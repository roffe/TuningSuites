using Avalonia.Controls;
using Avalonia.Input;
using SuiteApp.ViewModels;

namespace SuiteApp.Views;

public partial class CompareListView : UserControl
{
    public CompareListView() => InitializeComponent();

    private void OnDoubleTapped(object? sender, TappedEventArgs e) => _ = ((CompareListViewModel)DataContext!).Open(Grid.SelectedItem as CompareListRow);

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        _ = ((CompareListViewModel)DataContext!).Open(Grid.SelectedItem as CompareListRow);
    }
}
