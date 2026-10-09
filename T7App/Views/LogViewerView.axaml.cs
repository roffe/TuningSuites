using System;
using Avalonia.Controls;
using T7App.ViewModels;

namespace T7App.Views;

public partial class LogViewerView : UserControl
{
    public LogViewerView() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is LogViewerViewModel vm) Graph.SetData(vm.Channels, vm.Start);
    }
}
