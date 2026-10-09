using Avalonia.Controls;
using Avalonia.Interactivity;
using SuiteApp.Services;
using T7App.ViewModels;

namespace T7App.Views;

public partial class AirmassResultView : UserControl
{
    public AirmassResultView() => InitializeComponent();

    private async void OnCompare(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window w && DataContext is AirmassResultViewModel vm && await Dialogs.OpenFile(w, "binary files", "*.bin") is { } file)
            vm.Compare(file);
    }
}
