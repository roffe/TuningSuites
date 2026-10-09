using Avalonia.Controls;
using Avalonia.Input;
using CommonSuite;
using T7App.ViewModels;

namespace T7App.Views;

public partial class SearchResultsView : UserControl
{
    public SearchResultsView() => InitializeComponent();

    private void OnDoubleTapped(object? sender, TappedEventArgs e) =>
        ((SearchResultsViewModel)DataContext!).Open(Grid.SelectedItem as SymbolHelper);
}
