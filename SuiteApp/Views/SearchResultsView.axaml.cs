using Avalonia.Controls;
using Avalonia.Input;
using CommonSuite;
using SuiteApp.ViewModels;

namespace SuiteApp.Views;

public partial class SearchResultsView : UserControl
{
    public SearchResultsView() => InitializeComponent();

    private void OnDoubleTapped(object? sender, TappedEventArgs e) =>
        ((SearchResultsViewModel)DataContext!).Open(Grid.SelectedItem as SymbolHelper);
}
