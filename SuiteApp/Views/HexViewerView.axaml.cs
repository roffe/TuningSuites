using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SuiteApp.ViewModels;

namespace SuiteApp.Views;

public partial class HexViewerView : UserControl
{
    public HexViewerView()
    {
        InitializeComponent();
        Editor.Caret.LocationChanged += (_, _) => (DataContext as HexViewerViewModel)?.CaretAt(Editor.Caret.Location.ByteIndex);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is HexViewerViewModel vm)
        {
            Editor.Document = vm.Document;
            vm.CaretAt(0);
        }
    }

    private void OnSave(object? sender, RoutedEventArgs e) => (DataContext as HexViewerViewModel)?.Save();
}
