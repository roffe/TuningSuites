using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using T8App.ViewModels;

namespace T8App.Views;

public partial class PidEditorWindow : Window
{
    public PidEditorWindow() => InitializeComponent();

    private PidEditorViewModel Vm => (PidEditorViewModel)DataContext!;

    // the PID editor's columns or the TEM editor's
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not PidEditorViewModel vm) return;
        foreach (DataGridColumn c in Grid.Columns)
            c.IsVisible = c.Header is not string h || (vm.IsTem ? h is not ("PID" or "Read" or "Write") : h is not ("Label" or "Type"));
    }

    private void OnFind(object? sender, TextChangedEventArgs e) => Vm.Filter(FindBox.Text ?? "");

    private void OnOk(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
